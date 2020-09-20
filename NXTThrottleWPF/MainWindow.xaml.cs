using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows;

using Microsoft.FlightSimulator.SimConnect;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;

namespace NXTThrottleWPF {
    public enum DEFINITIONS {
        PlaneThrottle
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct StructPlaneThrottle { //only using eng1 for now for ease.
        public double ENG1;
        public double ENG2;
    };

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow:Window {

        SimConnect simConnect;
        bool connectedToSim = false;

        /// Window handle
        IntPtr handle;
        HwndSource handleSource;

        const int WM_USER_SIMCONNECT = 0x0402;

        //thread that gets and sends throttle continuously
        Thread pollThread;

        /// <summary>
        /// Constructor and starting for the window
        /// </summary>
        public MainWindow() {
            InitializeComponent();

            handle = new WindowInteropHelper(this).EnsureHandle(); // Get handle of main WPF Window
            handleSource = HwndSource.FromHwnd(handle); // Get source of handle in order to add event handlers to it
            handleSource.AddHook(HandleSimConnectEvents);

            pollThread = new Thread(PollThread);
        }

        ~MainWindow() {
            if (handleSource != null) {
                handleSource.RemoveHook(HandleSimConnectEvents);
            }
        }

        private IntPtr HandleSimConnectEvents(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam, ref bool isHandled) {
            isHandled = false;

            switch (message) {
                case WM_USER_SIMCONNECT: {
                        if (simConnect != null) {
                            simConnect.ReceiveMessage();
                            isHandled = true;
                        }
                    }
                    break;

                default:
                    break;
            }

            return IntPtr.Zero;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e) {
            if (!connectedToSim) {

                try { 
                    simConnect = new SimConnect("Managed Data Request", handle, WM_USER_SIMCONNECT, null, 0);
                } catch (COMException ex) {
                    ConnectButton.Content = "Error connecting";
                    Console.WriteLine("ERROR CONNECTING TO SIMCONNECT: " + ex.ToString());
                }

                if (simConnect != null) {
                    /// Listen to connect and quit msgs
                    simConnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(SimConnect_OnRecvOpen);
                    simConnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(SimConnect_OnRecvQuit);

                    /// Listen to exceptions
                    simConnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(SimConnect_OnRecvException);

                    /// Catch a simobject data request
                    //simConnect.OnRecvSimobjectDataBytype += new SimConnect.RecvSimobjectDataBytypeEventHandler(SimConnect_OnRecvSimobjectDataBytype);

                    //set up the data definitions and things
                    simConnect.AddToDataDefinition(DEFINITIONS.PlaneThrottle, "GENERAL ENG THROTTLE LEVER POSITION:1", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                    simConnect.AddToDataDefinition(DEFINITIONS.PlaneThrottle, "GENERAL ENG THROTTLE LEVER POSITION:2", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<StructPlaneThrottle>(DEFINITIONS.PlaneThrottle);
                }
                
            } else {
                if (simConnect != null) {
                    simConnect.Dispose();
                    simConnect = null;
                }
            }
        }

        private void SimConnect_OnRecvSimobjectDataBytype(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data) {
            throw new NotImplementedException();
        }

        private void SimConnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data) {
            SIMCONNECT_EXCEPTION eException = (SIMCONNECT_EXCEPTION)data.dwException;
            Console.WriteLine("SimConnect_OnRecvException: " + eException.ToString());

            OutputTextBlock.Text += "SimConnect_OnRecvException: " + eException.ToString();
        }

        private void SimConnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data) {
            Console.WriteLine("SimConnect_OnRecvQuit");
            Console.WriteLine("Disconnected to KH");

            ConnectButton.Content = "Connect";
            connectedToSim = false;
        }

        private void SimConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data) {
            Console.WriteLine("SimConnect_OnRecvOpen");
            Console.WriteLine("Connected to KH");

            ConnectButton.Content = "Disconnect";
            connectedToSim = true;
        }

        private void SendThrottle_Clicked(object sender, RoutedEventArgs e) {
            if (!pollThread.IsAlive) {
                pollThread.Start();
                Console.WriteLine("Polling started");
            } else {
                Console.WriteLine("polling ended.");
                pollThread.Abort();
            }
        }

        private void PollThread() {
            while (true) {
                double throttleAmount = NXTcontroller.getThrottlePercent();
                if (throttleAmount != -1) {
                    //OutputTextBlock.Text = "Throttle is at: " + (Double)throttleAmount;
                    StructPlaneThrottle planeThrottle = new StructPlaneThrottle() {
                        ENG1 = throttleAmount,
                        ENG2 = throttleAmount
                    };
                    if (connectedToSim) {
                        simConnect.SetDataOnSimObject(DEFINITIONS.PlaneThrottle, 1, SIMCONNECT_DATA_SET_FLAG.DEFAULT, planeThrottle);
                    }
                } else {
                    //OutputTextBlock.Text = "Error getting motor position.";
                }
                Thread.Sleep(100);
            }
            Console.WriteLine("Thread ended");
        }
    }
}
