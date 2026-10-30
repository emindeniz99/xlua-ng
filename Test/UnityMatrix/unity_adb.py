"""The environment for the Unity editors the test harness starts.

Unity's Android module starts its bundled adb, whose version follows the
editor (32.0.0 in 2021.3 and 2022.3, 36.0.0 in 6000.3, 37.0.1 in 6000.6). An
adb client of another version kills the running server on the default port
5037, and with it the device connections of every other tool on this machine,
such as a phone benchmark or another session's adb. The harness never needs
Unity's adb (the scripts install and run players with their own), so Unity gets
its own server port, and that server does not connect to wireless devices on
its own (mDNS auto-connect off), so it leaves the test phone to the shared
server.
"""

import os

UNITY_ADB_PORT = "5038"


def unity_env(env=None):
    """env (default os.environ) with Unity's adb moved off the shared server."""
    return dict(os.environ if env is None else env,
                ANDROID_ADB_SERVER_PORT=UNITY_ADB_PORT, ADB_MDNS_AUTO_CONNECT="0")
