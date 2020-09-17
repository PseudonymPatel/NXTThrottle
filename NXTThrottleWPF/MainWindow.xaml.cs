using System;
using System.Windows;

using Microsoft.FlightSimulator.SimConnect;
using System.Runtime.InteropServices;

namespace NXTThrottleWPF {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow:Window {

        SimConnect simConnect;
        bool connectedToSim = false;

        /// Window handle
        private IntPtr m_hWnd = new IntPtr(0);

        /// <summary>
        /// Constructor and starting for the 
        /// </summary>
        public MainWindow() {
            InitializeComponent();

            //NXTControl nxtController = new NXTControl();
            //nxtController.ConnectAndInitialize();
            //nxtController.ContinuouslyPoll();
        }

        //this needs to be refactored
        public void SetWindowHandle(IntPtr _hWnd) {
            m_hWnd = _hWnd;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e) {
            if (!connectedToSim) {

                const int WM_USER_SIMCONNECT = 0x0402;

                try { 
                    simConnect = new SimConnect("Managed Data Request", m_hWnd, WM_USER_SIMCONNECT, null, 0);
                } catch (COMException ex) {
                    ConnectButton.Content = "Error connecting";
                    Console.WriteLine("ERROR CONNECTING TO SIMCONNECT: " + ex.ToString());
                }

                if (simConnect != null) {
                    ConnectButton.Content = "Connected";
                    connectedToSim = true;
                }
                
            } else {
                if (simConnect != null) {
                    simConnect.Dispose();
                    simConnect = null;
                }
                ConnectButton.Content = "Disconnected";
                connectedToSim = false;
            }
        }
    }
}
