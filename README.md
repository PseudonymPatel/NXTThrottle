# NXTThrottle
A throttle quadrant made using Lego Mindstorms NXT

Only runs on windows (ofc). Connects over usb, as I finally sat down and figured it out. proper instructions/guide/readme soon? I am not expecting anyone else to use it anyways, more here just as backup/reference for how to do nxt stuff if anyone else wants to see an implementation of Lego mindstorms nxt direct command communication.

* There is not EV3 support, idk if it is backwards compatible, I have no idea if it is easy or not, make a issue if you want me to look at it, shouldn't be too bad unless there are fundamental changes to the system...
# Building
To run this, make sure you have msfs installed and working, move the simConnect.dll from the SimConnect SDK folder into the project folder. You need the LibUsbDotNet library too, this is something you can online. I use 2.2.0 (something like that) or whatever the latest version 2 is.

# Using normally
Download the latest release from the releases page on the right, then unzip and run the .exe. It DOES NOT need any permissions to run (except access to SimConnect).
* I removed the ability to use bluetooth because it is hard to do both, but I left the important bluetooth code if you want to do it yourself. 
* Ignore the text box where you enter com port, it doesn't do anything. was for bluetooth.
