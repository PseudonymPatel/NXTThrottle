using System;
using System.Windows;

using Microsoft.FlightSimulator.SimConnect;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;

namespace NXTThrottleWPF {
    public enum DEFINITIONS {
        PlaneThrottle,
        PlanePitchYaw
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct StructPlaneThrottle {
        public double ENG1;
        public double ENG2;
    };
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct StructPlanePitchYaw {
        public double ELEVATOR; 
        public double AILERON;
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

        //timer that keeps nxt awake constantly.
        Timer keepAwakeTimer;

        //boolean that lets the polling thread end gracefully.
        bool continuePolling = false;

        /// <summary>
        /// Constructor and starting for the window
        /// </summary>
        public MainWindow() {
            InitializeComponent();

            handle = new WindowInteropHelper(this).EnsureHandle(); // Get handle of main WPF Window
            handleSource = HwndSource.FromHwnd(handle); // Get source of handle in order to add event handlers to it
            handleSource.AddHook(HandleSimConnectEvents);

            //thread that handles continueously sending/polling NXT data to sim. Relies on everything set up.
            pollThread = new Thread(PollThread);

            //Timer that periodically sends packet to keep the NXT awake/not go to sleep. Runs every 1 2/3 min because sleep setting least amount is 2 min.
            keepAwakeTimer = new Timer(keepAwakeTimerFunction, null, Timeout.Infinite, Timeout.Infinite);
        }
        ~MainWindow() { //destructor for window
            if (handleSource != null) {
                handleSource.RemoveHook(HandleSimConnectEvents);
            }
        }

        private IntPtr HandleSimConnectEvents(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam, ref bool isHandled) {
            isHandled = false;

            switch (message) {
                case WM_USER_SIMCONNECT: {
                        if (simConnect != null) {
                            //TODO: handle error message on sim close.
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

                    simConnect.AddToDataDefinition(DEFINITIONS.PlanePitchYaw, "ELEVATOR POSITION:1", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                    simConnect.AddToDataDefinition(DEFINITIONS.PlanePitchYaw, "AILERON POSITION:1", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                    simConnect.RegisterDataDefineStruct<StructPlanePitchYaw>(DEFINITIONS.PlanePitchYaw);
                }
                
            } else {
                if (simConnect != null) {
                    continuePolling = false;
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
                continuePolling = true;
                pollThread = new Thread(PollThread);
                pollThread.Start();
                keepAwakeTimer.Change(0, 100000); //change timer to start now. (every 1 2/3 min)
                Console.WriteLine("Polling started");
            } else {
                Console.WriteLine("polling ended.");
                //rather than directly abort, instead make it finish an iteration before ending.
                continuePolling = false;
                keepAwakeTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private void PollThread() {
            double[] prevs = { 0, 0, 0 };
            bool[] hasNew = { false, false, false };

            while (continuePolling) {
                double[] joyStickAmounts = NXTcontroller.getAxisPositions();
                Console.WriteLine("Throttle is at: " + joyStickAmounts[0] + ", Pitch: " + joyStickAmounts[1] + ", Yaw: " + joyStickAmounts[2]);

                for (int i = 0; i < 2; i++) {
                    if (joyStickAmounts[i] != -1 && prevs[i] != joyStickAmounts[i]) {
                        hasNew[i] = true;
                    }
                }
                if (hasNew[0]) {
                    StructPlaneThrottle planeThrottle = new StructPlaneThrottle() {
                        ENG1 = joyStickAmounts[0],
                        ENG2 = joyStickAmounts[0]
                    };
                    if (connectedToSim) {
                        simConnect.SetDataOnSimObject(DEFINITIONS.PlaneThrottle, 1, SIMCONNECT_DATA_SET_FLAG.DEFAULT, planeThrottle);
                        prevs[0] = joyStickAmounts[0];
                    }
                }
                if (hasNew[1] || hasNew[2]) {
                    StructPlanePitchYaw planePY = new StructPlanePitchYaw {
                        ELEVATOR = joyStickAmounts[1],
                        AILERON = joyStickAmounts[2]
                    };
                    if (connectedToSim) {
                        simConnect.SetDataOnSimObject(DEFINITIONS.PlanePitchYaw, 1, SIMCONNECT_DATA_SET_FLAG.DEFAULT, planePY);
                        prevs[1] = joyStickAmounts[1];
                        prevs[2] = joyStickAmounts[2];
                    }
                }
                Thread.Sleep(2);
            }
        }

        private void keepAwakeTimerFunction(object state) {
            byte[] keepAwakePacket = { 0x80, 0x0D }; //sends keep awake packet with no response requested.
            NXTcontroller.SendPacket(keepAwakePacket);
        }
    }
}
