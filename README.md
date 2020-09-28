# NXTThrottle
A throttle quadrant made using Lego Mindstorms NXT

Only runs on windows (ofc). Must use bluetooth to connect to NXT due to USB being hard to work with and being pushed for time. It's a feature not a bug!!

* There is not EV3 support, idk if it is backwards compatible, I can do this if you make a github issue ~~(10 seconds you lazy ass)~~
# Building
To run this, make sure you have msfs installed and working, move the simConnect.dll from the SimConnect SDK folder into the project folder. Also make sure to get the right com 
port in the NXT file. Then run. 

# Using normally
Download the latest release from the releases page on the right, then unzip and run the .exe. It DOES NOT need any permissions to run (except access to SimConnect and Bluetooth).
* If your outbound bluetooth COM port is not COM3 for the NXT, then either build yourself with the right com port (command + f in the NXTControl file for "COM3" to find where to replace) OR just wait for the next release, I'll stick it there.
