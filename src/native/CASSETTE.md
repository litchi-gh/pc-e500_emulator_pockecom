# Cassette signal endpoint

The native core transports physical CI/CO levels. It does not translate tape
bytes, filenames, blocks or checksums. A pulse is a positive duration in CPU
cycles (768,000 cycles/second) and a level0/1. Adjacent pulses may have the same
level, for example when splitting a silence longer than UINT32_MAX cycles.

CI is visible in SSR bit1 only while SCR BZ2 is enabled. SCR modes6/7 mirror
CI onto CO and the actual48kHz audio stream. Playback and capture advance at
individual cycles inside peripheral steps, including during HALT/OFF; an
external recorder does not use the UART's clock. Recording observes the same
CO source as the11-pin bus and audio oscillator.

All functions use the existing C ABI and a Machine pointer:

| Function | Result |
|---|---|
| `e500_tape_connect(m,on)` |1 on success,0 for invalid input |
| `e500_tape_remote(m,on)` | Select manual0(default) or CE-126P remote1;0 rejects invalid values or an active/armed transfer |
| `e500_tape_load(m,uint32_t *cycles,uint8_t *levels,int count)` |1 if the entire waveform was accepted;0 retains existing input |
| `e500_tape_append(m,uint32_t *cycles,uint8_t *levels,int count)` |1 appends a complete pulse chunk, including during playback;0 retains input |
| `e500_tape_play(m)` |1 starts/resumes loaded pulses;0 while offline, empty, already playing or recording |
| `e500_tape_record(m)` |1 starts CO capture;0 while offline, playing or already recording |
| `e500_tape_stop(m)` |1 stops playback/capture, retaining pending input and captured output |
| `e500_tape_read(m,uint32_t *cycles,uint8_t *levels,int count)` | Number of completed captured pulses drained into the two arrays |
| `e500_tape_status(m)` | Bit0 connected,1 playing,2 recording,3 input pending,4 capture overflow,5 output pending,6 remote cable enabled,7 external motor running |

Playback resumes the remaining duration of a partially played pulse. Loading
zero pulses after stopping unloads the input. Load rejects zero durations,
levels other than0/1 and more than1,048,576 pulses before changing input.
Append has the same validation and queue bound. It retains the remaining
duration of the current pulse, allowing long tapes to be supplied in chunks
without an artificial silence. Queue the next chunk before input is exhausted.
Recording and playback are mutually exclusive. Capture buffers at most
1,048,576 completed pulses; drain output during long recordings. Overflow
stops capture, retains its complete prefix and sets the status bit.

In remote mode, play/record arms the deck and a stopped motor freezes tape
position. CE-126P device code10h starts the motor and00h stops it, after all
eight LSB-first bits and the end of the device selection. Partial codes,
tones, and other device codes do not change the motor. Input is quiet while
stopped; recording ignores changes made while tape is stationary and counts
only moving time. A restart retains a partly consumed pulse and continuous
recorded levels. Manual mode advances independently of the motor.

Disconnecting CE-126P stops the motor immediately. CPU reset and state load
preserve the powered external motor and the user's remote-cable preference;
neither is rewound with CPU snapshots. No snapshot format change is needed.
An armed remote transfer still rejects snapshots, including when stopped.

Snapshots are refused while input remains queued or tape I/O is active. A
CPU reset stops tape I/O and unloads input; it preserves connection and already
captured external output. Loading an idle CPU snapshot also preserves captured
output. External recording history is never rewound by machine snapshots.

Run `Emulator/tools/test-cassette.cmd` for the signal model and
`Emulator/tools/test-native.cmd` for the real Machine/API, CI gate,48kHz audio,
exact oscillator edges and reset/snapshot behavior. Neither uses a stock ROM.
This endpoint does not yet provide desktop WAV controls. CustomROM implements
physical CAS blocks/headers, remote commands/start-stop waits and the
CAS special-file/FCS layer on this endpoint; see
[the ROM driver and its native/C validation](../../../CustomROM/TAPE_DESIGN.md).
