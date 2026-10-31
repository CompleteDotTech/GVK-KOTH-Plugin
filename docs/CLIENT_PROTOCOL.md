# Zone state sent to players

The plugin sends KOTH zone state to every player for the GVK_Settings KOTH HUD, the `/gvk` map, minimap and compass icons, and fireworks. The GVK_Settings client script listens with `MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(47410, ...)`.

## When
- Once a second while any zone is open; every 10 seconds otherwise.
- Right away when a capture bar completes (with a `C` line).
- A player who joins gets the next message within those intervals; nothing needs to be requested.

## Format
UTF-8 text, one item per line, fields separated by `|`. Numbers use `.` as the decimal point. Names never contain `|`.

```
GVKKOTH|1
O|KharToba|4115.2|38891.6|26152.7|1500|0.420|3|0|[AAA] [BBB]
N|Kalash|8815.5|14612.7|35354.0|1500|639340596000000000
C|KharToba|4115.2|38891.6|26152.7|[AAA] [BBB]
```

| Line | Fields | Meaning |
|---|---|---|
| `GVKKOTH|1` | version | Header. Ignore messages with another version. |
| `O` | name, x, y, z, radius (m), bar (0-1), counted participants, grid entity id, optional current faction tags | An open zone. NPC zones send the grid's current centre and its entity id (0 for fixed zones); the client follows that grid every frame when it is loaded, for a smooth boundary on moving zones, and uses x/y/z otherwise. |
| `N` | name, x, y, z, radius (m), start (UTC `DateTime.Ticks`) | The next slot's zone, already picked, not yet open. At most one. |
| `C` | name, x, y, z, faction tags | A bar completed at this zone (fireworks). |

A zone missing from the latest message is no longer open. Each message is the full state, so the client replaces its list each time.

### Open-zone faction tags and compatibility

The optional tenth field of an `O` line (counting `O` as the first field) lists the eligible factions represented by the zone's current counted participants. It is refreshed during capture scans, including scans with no eligible factions, and is independent of whether a bar completes. It is not a list of past winners or factions from `C` events.

Tags are distinct, sorted ordinally and written in brackets separated by spaces, for example `[AAA] [BBB]`. Null or blank tags are omitted; each tag is trimmed, `|` is replaced with `/`, CR and LF with spaces, and `<` and `>` are removed before empty results are omitted and duplicates are removed. Zone names retain their existing cleaning: `|` becomes `/`, and CR and LF become spaces.

An extended sender includes the tenth field even when it is empty: `O|name|x|y|z|radius|bar|participants|gridId|` means there are no eligible current factions. Legacy eight-field lines omit the grid entity id and faction field; legacy nine-field lines include the grid id but omit the faction field. In both legacy forms, current faction information is unavailable, which is different from a present, empty field. New receivers should accept all three forms and preserve that distinction. Existing receivers can continue reading the original fields and ignore the appended field; new receivers work with old senders without deriving current factions from `C` lines. The header remains `GVKKOTH|1`, and `C` continues to report a completed bar for fireworks.
