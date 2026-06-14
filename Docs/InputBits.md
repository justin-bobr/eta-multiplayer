# InputBits

Held-input bitfield for subtick movement. Each bit is set while the corresponding key is down. Press-edges (Jump/Crouch/Fire/Reload/Inspect) are detected by the driver from the 0→1 transition between consecutive `StateAfter` masks, so there is no separate "pressed" bit.
