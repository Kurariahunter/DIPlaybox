# Lua decoder and viewer

Use Open Lua for a loose Lua/configuration file, View Lua for one selected archive member, or double-click
a .lua, .luac, .dnax or .sx member. Other archive entries retain the existing double-click extraction behavior.
Lua files also open via drag/drop and the command-line file argument. Archive members are read directly
without temporary extraction or modification of the game/archive.

The read-only viewer highlights Lua keywords, strings, numbers, line comments and long-bracket strings/comments.
It has Lua, Bytecode, Constants and bounded Hex tabs, Find Next / Ctrl+F, Copy View and Export Text Copy.
Exports cannot overwrite the original opened source or archive. No bytecode compiler, automatic archive
replacement, source editing, game-script execution or automatic Gateway/ActionRouter decryption is included.

## Native decoding

Plain UTF-8 (BOM optional) and BOM-marked UTF-16 source are supported. Lua 5.1 chunks support little/big endian,
32-bit instructions/integers, 32/64-bit size_t and 32/64-bit floating-point numbers. Header/layout, lengths,
counts and prototype depth are checked. Strings retain byte identity through Lua decimal escapes.
Literal table constructors are reconstructed into readable Lua, preserving numeric versus string table keys,
SETLIST array positions and nil-deletion behavior. This covers many EmuInfo, progression, toolset and DNA files.

Function/control-flow chunks are not presented as fake recovered source. The viewer shows the bytecode and
constants, explicitly reports why literal reconstruction is unavailable, and offers an optional full decompiler.
Unknown Lua versions/layouts report their limitations and retain the Hex preview. Input limit is 16 MiB,
readable output limit 8 MiB per view; on-screen previews cap at 1,000,000 characters with complete generated
text available through Copy/Export. Hex shows the first 64 KiB.

## Optional unluac

Click Decompile with unluac and select your trusted unluac.jar. Java must be available on PATH.
The app invokes java -jar using structured arguments, a temporary copy of the original bytes, bounded output
and a 60-second timeout. It does not run the resulting Lua. Temporary input is removed when finished.
The last JAR path is saved under LocalAppData/DIPlaybox. No JAR, Java runtime or third-party decompiler code
is bundled. Tool source/credits: https://sourceforge.net/projects/unluac/
External decompilation is a research aid, not proof of a byte-for-byte or gameplay-equivalent round trip.
Encrypted string literals need the separately reviewed Gateway/ActionRouter decoding workflow.
