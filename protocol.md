# HandUMI Quest App wire protocol

## Pose stream

- Transport: TCP
- Quest listen port: `65432`
- Direction: PC connects to Quest; Quest sends frames
- Encoding: UTF-8
- Framing: one JSON object followed by `\n`
- Serialization: Unity `JsonUtility.ToJson(PoseData)`

### JSON keys

`hmdPosition`, `hmdRotation`, `leftControllerPosition`, `leftControllerRotation`, `rightControllerPosition`, `rightControllerRotation`, `leftTriggerPressed`, `rightTriggerPressed`, `leftGripPressed`, `rightGripPressed`, `buttonAPressed`, `buttonBPressed`, `buttonXPressed`, `buttonYPressed`, `leftJoystick`, `rightJoystick`, `leftThumbstickClick`, `rightThumbstickClick`, `leftThumbstickTouched`, `rightThumbstickTouched`, `buttonATouched`, `buttonBTouched`, `buttonXTouched`, `buttonYTouched`, `leftIndexTriggerTouched`, `rightIndexTriggerTouched`, `ovrTimeNs`, `unityTimeNs`, `deltaTime`, `leftTracked`, `rightTracked`, `leftValid`, `rightValid`, `leftBattPct`, `rightBattPct`, `hmdBattPct`, `hmdCharging`, `startPressed`, `backPressed`.

Unity vectors serialize as `{ "x": ..., "y": ..., "z": ... }`; quaternions add `w`.

## Time synchronization

- Transport: UDP
- Quest listen port: `42000`
- Endianness: little-endian

Request, 9 bytes:

| Offset | Size | Meaning |
|---:|---:|---|
| 0 | 1 | packet ID `1` |
| 1 | 8 | PC monotonic timestamp `t1` |

Response, 17 bytes:

| Offset | Size | Meaning |
|---:|---:|---|
| 0 | 1 | packet ID `2` |
| 1 | 8 | echoed `t1` |
| 9 | 8 | Quest runtime timestamp in nanoseconds |

Equivalent Python layouts: request `<BQ`, response `<BQQ`.
