# SnapshotFieldFlags

`Vantix.Net.SnapshotFieldFlags`

Per-player field mask for delta-baseline snapshot compression. Bit = 1 sends that field group, 0 keeps the baseline value. Groups bundle fields that change together (Pos+Vel, Yaw+Pitch). `All` = full snapshot (player absent from the baseline). 13 bits fit a ushort.
