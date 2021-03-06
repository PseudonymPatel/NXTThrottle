using System;
using System.IO.Ports;
using System.Linq;
using System.Windows;

using LibUsbDotNet.Main;
using LibUsbDotNet;
using System.Text;
using System.Threading;

namespace NXTThrottleWPF {
    /// <summary>
    /// This part of the partial class deals with connecting to the NXT, including the calibration sequence.
    /// </summary>
    public partial class MainWindow {
        //this int will keep track of the setup progress, don't really want to use an enum. 

        //0 = nothing, 1 = back calibration ready, 2 = forwards calibration ready, 3 = all ready to use.
        private int setupProgress = 0;

        NXTControl NXTcontroller = new NXTControl();

        private void SetupButton_Clicked(object sender, RoutedEventArgs e) {
            switch (setupProgress) {
                case 3: //also peform this step to recalibrate
                case 0: //connect to the NXT and prep for back calibration
                    if (setupProgress == 3) NXTcontroller.Disconnect();
                    
                    string comPortToConnectTo = ComPortTextBox.Text;
                    
                    bool connectSuccess = NXTcontroller.Connect(NXTcontroller, comPort: comPortToConnectTo);
                    //TODO: recalibration once already connected does not work.

                    if (connectSuccess) {
                        CalibrateButtonLabel.Text = "Move fully backwards and hit button again.";
                        setupProgress = 1;
                    } else {
                        OutputTextBlock.Text = "Error connecting to NXT, retry.";
                    }

                    break;
                case 1: //do back calibration
                    NXTcontroller.InitializeBackwards();

                    CalibrateButtonLabel.Text = "Move FORWARDS at hit button.";

                    setupProgress = 2;
                    break;
                case 2: //do forwards calib
                    byte[] packet = { 0x00, 0x06, 0x00 };
                    NXTcontroller.SendPacket(packet);
                    packet[2] = 0x01;
                    NXTcontroller.SendPacket(packet);
                    packet[2] = 0x02;
                    NXTcontroller.SendPacket(packet);
                    
                    CalibrateButtonLabel.Text = "Done calibrating! Hit to recalibrate";

                    setupProgress++;
                    break;
                default:
                    CalibrateButtonLabel.Text = "Pester the developer. You aren't supposed to see this.";
                    break;
            }
        }
    }

    class NXTControl {
        private static SerialPort _serialPort;
        private static readonly byte[] motorPositionPacket = { 0x00, 0x06, 0x00 };
        private bool useBluetooth = false;
        private bool[] isSetup = { false, false, false };

        //USB-related connection things
        private UsbDevice MyUsbDevice;
        private static UsbDeviceFinder MyUsbFinder = new UsbDeviceFinder(0x0694, 2); //vendor, pid of NXT
        private UsbEndpointReader usbReader;
        private UsbEndpointWriter usbWriter;

        private static readonly int MAX_ROTATION_LEEWAY = 2; //degrees of "leeway" to give to the motor. can be X degrees less than max to show as max. Accounts for minor inconsistencies in max position.
        private int maxRotation = 100;
        private int directionOfRotation = 1; //either 1 or -1, depending on which way the motor rotates (if throttle forwards = motor backwards, then -1)
        private int throttleRotation = -1;

        private static int yawMaxRot = 100;
        private int yawDirectionOfRotation = 1; //either 1 or -1, depending on which way the motor rotates (if throttle forwards = motor backwards, then -1)
        private int yawRotation = -1;

        private static int pitchMaxRot = 100;
        private int pitchDirectionOfRotation = 1; //either 1 or -1, depending on which way the motor rotates (if throttle forwards = motor backwards, then -1)
        private int pitchRotation = -1;

        ~NXTControl() {
            Console.WriteLine("[DEBUG] Disposed of a NXTControl class.");
            Disconnect();
        }

        //The following are the bytes that can be sent to the nxt
        //reference sheet: http://kio4.com/b4a/programas/Appendix%202-LEGO%20MINDSTORMS%20NXT%20Direct%20commands.pdf

        public bool Connect(NXTControl nxt, string comPort = "COM3") {
            //thread that constantly monitors user commands in application.
            //Thread keyboardThread = new Thread(KeyboardListener);
            //keyboardThread.IsBackground = true;

            if (useBluetooth) {
                //connect over bluetooth
                _serialPort = new SerialPort(comPort);
                Console.WriteLine("Serial Port Initialized");

                //attempt to open the serial port and start reading data.
                int errorCount = 0;
            openport: try {
                    _serialPort.Open();
                    Console.WriteLine("Serial port opened and listener started.");
                } catch (Exception e) {
                    //catch System.UnauthorizedAccessException that arises when already connected. 
                    if (e.GetType() == typeof(System.UnauthorizedAccessException)) {
                        Console.WriteLine("Already connected or line busy, assuming already connected.");
                        return true;
                    }
                    Console.WriteLine("ERROR STARTING: " + e.ToString());
                    errorCount += 1;
                    if (errorCount < 5) {
                        goto openport; //I hate myself.
                    } else {
                        Console.WriteLine("TOO MANY ERRORS TRYING TO CONNECT, ABORTED");
                        return false;
                    }
                }
            } else {
                ErrorCode ec = ErrorCode.None;
                try {
                    // Find and open the usb device.
                    MyUsbDevice = UsbDevice.OpenUsbDevice(MyUsbFinder);

                    // If the device is open and ready
                    if (MyUsbDevice == null) {
                        Console.Error.WriteLine("[ERROR] Failed to connect over USB.");
                        return false;
                    }

                    // If this is a "whole" usb device (libusb-win32, linux libusb-1.0)
                    // it exposes an IUsbDevice interface. If not (WinUSB) the 
                    // 'wholeUsbDevice' variable will be null indicating this is 
                    // an interface of a device; it does not require or support 
                    // configuration and interface selection.
                    IUsbDevice wholeUsbDevice = MyUsbDevice as IUsbDevice;
                    if (!ReferenceEquals(wholeUsbDevice, null)) {
                        // This is a "whole" USB device. Before it can be used, 
                        // the desired configuration and interface must be selected.

                        Console.WriteLine("[DEBUG] This is whole usb device.");

                        // Select config #1
                        wholeUsbDevice.SetConfiguration(1);

                        // Claim interface #0.
                        wholeUsbDevice.ClaimInterface(0);
                    }

                    // open read and write endpoint 1.
                    usbReader = MyUsbDevice.OpenEndpointReader(ReadEndpointID.Ep02, 64, EndpointType.Bulk);
                    usbWriter = MyUsbDevice.OpenEndpointWriter(WriteEndpointID.Ep01, EndpointType.Bulk);

                    SetupReading();

                    Console.WriteLine("[DEBUG] Done initializing.");
                } catch (Exception ex) {
                    Console.WriteLine();
                    Console.WriteLine((ec != ErrorCode.None ? ec + ":" : String.Empty) + ex.Message);
                    Disconnect();
                }
            }

            //send a tone to nxt to notify user. 1khz for .5 seconds
            byte[] tonePacket = { 0x80, 0x03, 0xE8, 0x03, 0xF4, 0x01 };
            nxt.SendPacket(tonePacket);

            ////battery check message
            //byte[] message = { 0x00, 0x0B };

            ////attempt to send a battery check command
            //bool suc = SendPacket(message);
            //Console.WriteLine(suc);
            return true;
        }

        public void Disconnect() {
            if (MyUsbDevice != null) {
                if (MyUsbDevice.IsOpen) {
                    // If this is a "whole" usb device (libusb-win32, linux libusb-1.0)
                    // it exposes an IUsbDevice interface. If not (WinUSB) the 
                    // 'wholeUsbDevice' variable will be null indicating this is 
                    // an interface of a device; it does not require or support 
                    // configuration and interface selection.
                    IUsbDevice wholeUsbDevice = MyUsbDevice as IUsbDevice;
                    if (!ReferenceEquals(wholeUsbDevice, null)) {
                        // Release interface #0.
                        wholeUsbDevice.ReleaseInterface(0);
                    }

                    DestructReading();
                    MyUsbDevice.Close();
                }

                MyUsbDevice = null;

                // Free usb resources
                UsbDevice.Exit();
            }
        }

        public void InitializeBackwards() {
            //reset motor position packet.
            byte[] motorResetPacket = { 0x80, 0x0A, 0x00, 0x00 }; //relative is false, send without response
            SendPacket(motorResetPacket);
            motorResetPacket[2] = 0x01;
            SendPacket(motorResetPacket);
            motorResetPacket[2] = 0x02;
            SendPacket(motorResetPacket);
        }

        public double[] getAxisPositions() {
            double[] ret = { -1, -1, -1 };
            byte[] mp = { 0x00, 0x06, 0x00 };
            try {
                if (SendPacket(mp)) {
                    //return Math.Max(Math.Min(100, motorPosition / maxRotation), 0);  //Constrains to be between 0 and 100
                    double perc = Math.Min(100, (double)throttleRotation / maxRotation * 100);
                    if (perc < 7) {
                        ret[0] = 0;
                    } else {
                        ret[0] = perc;
                    }
                    //TODO: reverse throttle.
                }
                mp[2] = 0x01;
                if (SendPacket(mp)) { //pitch
                    double perc = (double)pitchRotation / pitchMaxRot * 100;
                    ret[1] = perc;
                }
                mp[2] = 0x02;
                if (SendPacket(mp)) { //yaw
                    double perc = (double)yawRotation / yawMaxRot * 100;
                    ret[2] = perc;
                }
            } catch (Exception e) {
                Console.WriteLine("ERROR GETTING THROTTLE: " + e.ToString());
                return ret;
            }
            return ret;
        }

        public void SetupReading() {
            usbReader.DataReceived += (OnRecievePacket);
            usbReader.DataReceivedEnabled = true;
        }

        public void DestructReading() {
            // Always disable and unhook event when done.
            usbReader.DataReceivedEnabled = false;
            usbReader.DataReceived -= (OnRecievePacket);
        }

        public byte[] ReadPacket() {
            if (useBluetooth) {
                try {
                    byte[] returnBuffer = new byte[30];
                    int bytesWritten = _serialPort.Read(returnBuffer, 0, 30);
                    int totalBytesLeft = BitConverter.ToInt16(returnBuffer, 0) - 2 - bytesWritten; //the number of bytes left to read.
                    int writeIndex = 4; //where to add more bytes to.

                    //while there are bytes left
                    while (totalBytesLeft > 0) {
                        Console.WriteLine("Bytes Left: " + totalBytesLeft);
                        //if there's less than four
                        if (totalBytesLeft < 4) {

                            //if exactly 0, no more left to read, return
                            if (totalBytesLeft == 0) {
                                return returnBuffer;
                            }

                            //finish up with the last bytes
                            _ = _serialPort.Read(returnBuffer, writeIndex, totalBytesLeft);
                            return returnBuffer.Take(writeIndex + totalBytesLeft).ToArray(); //only take the amount of bytes written
                        }

                        //take four more bytes, subtract 4 from totalBytesLeft
                        totalBytesLeft -= _serialPort.Read(returnBuffer, writeIndex, 4);
                        writeIndex += 4;
                    }
                    return returnBuffer;
                } catch (TimeoutException) {
                    return null;
                }
            } else {
                return null;
            }
        }

        /// <summary>
        /// Helper function to send a bluetooth packet with the parameters as a message.
        /// </summary>
        /// <param name="bytesToSend">bytes, without the bluetooth bytes, to send.</param>
        /// <returns>True if it succeeds, false if it errors.</returns>
        public bool SendPacket(byte[] bytesToSend) {
            if (useBluetooth) {
                try {
                    //get the length and convert to hex.
                    int lengthOfBytes = bytesToSend.Length;

                    //take the last first two bytes, reverse it. (Least significant first for bluetooth.)
                    byte[] hexLength = BitConverter.GetBytes(lengthOfBytes).Take(2).ToArray(); //.Reverse().ToArray(); //what the hell c#

                    //combine and send packet.
                    byte[] finalPacket = hexLength.Concat(bytesToSend).ToArray();
                    _serialPort.Write(finalPacket, 0, finalPacket.Length);
                    Console.WriteLine("Packet sent over serial: " + BitConverter.ToString(finalPacket));

                } catch (Exception error) {
                    Console.WriteLine("ERROR WHILE SENDING PACKET: " + error.ToString());
                    return false;
                }
            } else {
                ErrorCode ec;
                int bytesWritten;
                ec = usbWriter.Write(bytesToSend, 50, out bytesWritten);
                if (ec != ErrorCode.None) {
                    Console.Error.WriteLine("[ERROR] " + UsbDevice.LastErrorString);
                    return false;
                }
            }
            return true;
        }

        private void OnRecievePacket(object sender, EndpointDataEventArgs e) {
            //if the first byte is 0x02, it is a response packet
            //if the second byte is 0x06 it is response for a motor query
            byte[] recieved = new byte[e.Count];
            Buffer.BlockCopy(e.Buffer, 0, recieved, 0, e.Count);
            //Console.WriteLine("[DEBUG]: recieved: " + BitConverter.ToString(recieved));
            if (recieved[0] == 0x02) {
                //response packet
                switch (recieved[1]) {
                    case 0x06: //motor pos packet
                        int motorPos = BitConverter.ToInt32(recieved, 21);
                        if (isSetup[recieved[3]]) {
                            if (recieved[3] == 0x00) {
                                throttleRotation = motorPos * directionOfRotation;
                            } else if (recieved[3] == 0x01) {
                                pitchRotation = motorPos * -pitchDirectionOfRotation; //forwards = negative
                            } else if (recieved[3] == 0x02) {
                                yawRotation = motorPos * yawDirectionOfRotation;
                            }
                        } else {
                            if (recieved[3] == 0x00) {
                                if (motorPos < 0) {
                                    motorPos = Math.Abs(motorPos);
                                    directionOfRotation = -1;
                                }

                                maxRotation = motorPos - MAX_ROTATION_LEEWAY;
                                if (maxRotation < 1) { //avoids problems with dividing by zero or negatives.
                                    maxRotation = 1;
                                }
                                isSetup[0] = true;
                            } else if (recieved[3] == 0x01) {
                                if (motorPos < 0) {
                                    motorPos = Math.Abs(motorPos);
                                    pitchDirectionOfRotation = -1;
                                }

                                pitchMaxRot = motorPos;
                                if (pitchMaxRot < 1) { //avoids problems with dividing by zero or negatives.
                                    pitchMaxRot = 1;
                                }
                                isSetup[1] = true;
                            } else if (recieved[3] == 0x02) {
                                if (motorPos < 0) {
                                    motorPos = Math.Abs(motorPos);
                                    yawDirectionOfRotation = -1;
                                }

                                yawMaxRot = motorPos;
                                if (yawMaxRot < 1) { //avoids problems with dividing by zero or negatives.
                                    yawMaxRot = 1;
                                }
                                isSetup[2] = true;
                            }
                        }
                        return;
                    default:
                        Console.WriteLine("unknown type packet");
                        return;
                }
            }
        }
    }
}
