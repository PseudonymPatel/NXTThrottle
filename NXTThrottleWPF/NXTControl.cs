using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NXTThrottleWPF {
    /// <summary>
    /// This part of the partial class deals with connecting to the NXT, including the calibration sequence.
    /// </summary>
    public partial class MainWindow {
        //this int will keep track of the setup progress, don't really want to use an enum. 
        private int setupProgress = 0; //0 = nothing, 1 = back calibration ready, 2 = forwards calibration ready, 3 = all ready to use.
        NXTControl NXTcontroller = new NXTControl();

        private void SetupButton_Clicked(object sender, RoutedEventArgs e) {
            switch (setupProgress) {
                case 0: //connect to the NXT and prep for back calibration
                case 3: //also peform this step to recalibrate
                    //first connect over bluetooth
                    NXTcontroller.Connect();

                    CalibrateButtonLabel.Text = "Move fully backwards and hit button again.";

                    setupProgress = 1;
                    break;
                case 1: //do back calibration
                    NXTcontroller.InitializeBackwards();

                    CalibrateButtonLabel.Text = "Move FORWARDS at hit button.";

                    setupProgress = 2;
                    break;
                case 2: //do forwards calib
                    NXTcontroller.InitializeForwards();

                    CalibrateButtonLabel.Text = "Done calibrating! Hit to recalibrate";

                    setupProgress++;
                    break;
                default:
                    CalibrateButtonLabel.Text = "How'd you see this?";
                    break;
            }
        }
    }

    class NXTControl {
        static SerialPort _serialPort;
        static bool _continue = false;
        public static readonly byte[] motorPositionPacket = { 0x00, 0x06, 0x00 };

        static readonly int MAX_ROTATION_LEEWAY = 3; //degrees of "leeway" to give to the motor. can be X degrees less than max to show as max. Accounts for minor inconsistencies in max position.
        static int maxRotation = 100;
        private int directionOfRotation = 1; //either 1 or -1, depending on which way the motor rotates (if throttle forwards = motor backwards, then -1)

        //The following are the bytes that can be sent to the nxt
        //reference sheet: http://kio4.com/b4a/programas/Appendix%202-LEGO%20MINDSTORMS%20NXT%20Direct%20commands.pdf

        public void Connect() {

            //thread that constantly monitors user commands in application.
            //Thread keyboardThread = new Thread(KeyboardListener);
            //keyboardThread.IsBackground = true;

            //connect over bluetooth
            _serialPort = new SerialPort("COM3");
            Console.WriteLine("Serial Port Initialized");

            //attempt to open the serial port and start reading data.
            int errorCount = 0;
        openport: try {
                _serialPort.Open();
                _continue = true;
                Console.WriteLine("Serial port opened and listener started.");
            } catch (Exception e) {
                //catch System.UnauthorizedAccessException that arises when already connected. 
                if (e.GetType() == typeof(System.UnauthorizedAccessException)) {
                    Console.WriteLine("Already connected or line busy, assuming already connected.");
                    return;
                }
                Console.WriteLine("ERROR STARTING: " + e.ToString());
                errorCount += 1;
                if (errorCount < 5) {
                    goto openport; //I hate myself.
                } else {
                    Console.WriteLine("TOO MANY ERRORS TRYING TO CONNECT, ABORTED");
                    return;
                }
            }
        }

        /*
            //battery check message
            byte[] message = { 0x00, 0x0B };

            //attempt to send a battery check command
            SendPacket(message);
            byte[] response = ReadPacket();
            if (response != null && response.Length > 0) {
                Console.WriteLine(BitConverter.ToString(response));
            } else {
                Console.WriteLine("No packet recieved or error.");
            }
            */

        public void InitializeBackwards() {
            //reset motor position packet.
            byte[] motorResetPacket = { 0x80, 0x0A, 0x00, 0x00 }; //relative is false, send without response
            bool succeeded = SendPacket(motorResetPacket);

            //uncomment the next lines to recieve a response, also change first byte to 0x00 to get response. Leaving for debugging.
            //if (succeeded) {
            //    byte[] packet = ReadPacket();
            //    Console.WriteLine("Reset motor packet: " + BitConverter.ToString(packet));
            //} else {
            //    Console.WriteLine("Fuck");
            //}
        }

        public void InitializeForwards() {
            if (SendPacket(motorPositionPacket)) {
                byte[] packet = ReadPacket();
                if (packet != null && packet.Length > 0) {
                    //Console.WriteLine(BitConverter.ToString(packet));
                    int motorPosition = BitConverter.ToInt32(packet, 23);
                    Console.WriteLine("MAX MOTOR POSITION: " + motorPosition);

                    if (motorPosition < 0) {
                        motorPosition = Math.Abs(motorPosition);
                        directionOfRotation = -1;
                    }

                    maxRotation = motorPosition - MAX_ROTATION_LEEWAY;
                    if (maxRotation < 1) { //avoids problems with dividing by zero or negatives.
                        maxRotation = 1;
                    }
                }
            }

            //start keyboard thread/listener
            //keyboardThread.Start();
        }

        public double getThrottlePercent() {
            if (SendPacket(motorPositionPacket)) {
                byte[] packet = ReadPacket();
                if (packet != null && packet.Length > 0) {
                    //Console.WriteLine(BitConverter.ToString(packet));
                    int motorPosition = BitConverter.ToInt32(packet, 23) * directionOfRotation; //mult by directionOfRotation to make the correct sign.
                    //return Math.Max(Math.Min(100, motorPosition / maxRotation), 0);  //Constrains to be between 0 and 100
                    return Math.Min(100, Math.Abs((double)motorPosition / maxRotation * 100));
                }
            }
            return -1;
        }

        //public void ContinuouslyPoll() {
        //    //continuous get motor position packets.
        //    while (_continue) {
        //        if (SendPacket(motorPositionPacket)) {
        //            byte[] packet = ReadPacket();
        //            if (packet != null && packet.Length > 0) {
        //                //Console.WriteLine(BitConverter.ToString(packet));
        //                int motorPosition = BitConverter.ToInt32(packet, 23);
        //                Console.WriteLine("MOTOR POSITION: " + motorPosition + " Percent: " + Math.Min(100, Math.Abs((Double)(motorPosition / maxRotation))));
        //            }
        //        }
        //        Thread.Sleep(100);
        //    }
        //}

        public static byte[] ReadPacket() {
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
        }

        /// <summary>
        /// Helper function to send a bluetooth packet with the parameters as a message.
        /// </summary>
        /// <param name="bytesToSend">bytes, without the bluetooth bytes, to send.</param>
        /// <returns>True if it succeeds, false if it errors.</returns>
        public static bool SendPacket(byte[] bytesToSend) {
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
            return true;
        }
    }
}
