# Debt Collector - RimWorld Mod

A RimWorld 1.4/1.5 mod that adds "The Ledger" faction, a group of ruthless financiers who offer loans to colonies in need. Borrow silver, pay interest on time, or face brutal collections raids.

## Features

- **New Faction**: "The Ledger" spawns near your starting location
- **Loan System**: Request loans of 500, 1000, 2000, or 5000 silver via Comms Console
- **Interest Payments**: Pay periodic interest or face escalating consequences
- **Collections System**: Miss too many payments and face a hostile raid
- **Tribute System**: After forced collection, pay tribute to restore borrowing privileges

## Installation

1. Ensure you have the Harmony mod installed
2. Copy this mod folder to your RimWorld Mods directory
3. Build the C# project (see Building section)
4. Enable the mod in RimWorld's mod menu

## Building

### Requirements
- .NET Framework 4.8 SDK
- RimWorld game files for assembly references

### Build Steps

1. Set the `RIMWORLD_DIR` environment variable to your RimWorld installation path, OR edit the `.csproj` file to point to your RimWorld managed assemblies

2. Build using dotnet CLI:
```bash
cd Source/DebtCollector
dotnet build -c Release
```

3. The compiled DLL will be placed in the `Assemblies` folder

### Alternative: NuGet References
The project is configured to use `Krafs.Rimworld.Ref` NuGet package as a fallback for RimWorld assembly references.

## How to Test

### Quick Start Testing Checklist

1. **Enable Dev Mode** in RimWorld options

2. **Start a New Game**
   - Verify in the log: `[DebtCollector] Mod initialized. Harmony patches applied.`
   - Verify in the log: `[DebtCollector] Created Ledger settlement...` or `...already exists...`

3. **Check Faction Exists**
   - Open the Factions tab
   - Look for "The Ledger" faction

4. **Build a Comms Console**
   - Use dev mode to spawn: `CommsConsole`
   - Ensure it has power

5. **Test Loan Request**
   - Select the Comms Console
   - Click "Request Loan" gizmo
   - Select a loan amount
   - Verify silver spawns at trade drop spot
   - Verify letter received

6. **Test View Ledger**
   - Click "View Debt Ledger" gizmo
   - Verify dialog shows correct principal and status

7. **Test Interest Payment** (use dev mode to speed up)
   - Dev Mode → Debug Actions → Debt Collector → "Force Interest Due Now"
   - Wait one tick for the interest letter
   - Click "Pay Interest" gizmo
   - Verify success message

8. **Test Missed Payments**
   - Dev Mode → Debug Actions → Debt Collector → "Force Interest Due Now"
   - Dev Mode → Debug Actions → Debt Collector → "Skip to Payment Deadline"
   - Observe missed payment letter
   - Repeat to trigger collections

9. **Test Collections Raid**
   - Dev Mode → Debug Actions → Debt Collector → "Force Collections Raid Now"
   - Wait for raid to spawn
   - Defeat or wait for raiders to leave
   - Verify "Debt Settled By Force" letter
   - Verify status is now "Locked Out"

10. **Test Tribute**
    - With locked out status, click "Send Tribute" gizmo
    - Verify tribute accepted letter
    - Verify you can request loans again

### Dev Mode Debug Actions

All debug actions are under: **Dev Mode → Debug Actions → Debt Collector**

- `Force Interest Due Now` - Sets next interest tick to current tick
- `Force Collections State` - Puts active contract into collections
- `Force Collections Raid Now` - Triggers immediate raid
- `Grant 5000 Silver` - Spawns silver for testing payments
- `Reset Debt Contract` - Clears all debt state
- `Set Locked Out` - Sets locked out state
- `Log Debt Status` - Logs detailed contract state to console
- `Force Place Settlement` - Re-attempts settlement placement
- `Skip to Payment Deadline` - Sets payment deadline to now

### Console Commands

View detailed status in the debug log:
- Dev Mode → Debug Actions → Debt Collector → "Log Debt Status"

### Edge Case Testing

1. **No Comms Console**: Verify mod works but loan buttons unavailable
2. **Unpowered Console**: Verify gizmos don't appear
3. **No Silver**: Verify payment fails with appropriate message
4. **Multiple Loans**: Verify cannot take second loan while one active
5. **Save/Load**: Take a loan, save, reload, verify state preserved

## Settings

Access via Options → Mod Settings → Debt Collector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Interest Rate | 10% | Percentage of principal charged as interest |
| Interest Interval | 3 days | Days between interest payments |
| Payment Window | 24 hours | Time to pay after interest demand |
| Grace Missed Payments | 2 | Missed payments before collections |
| Collections Deadline | 18 hours | Time to pay after collections notice |
| Min Settlement Distance | 4 tiles | Minimum distance for Ledger settlement |
| Max Settlement Distance | 15 tiles | Maximum distance for Ledger settlement |
| Tribute Multiplier | 1.5x | Multiplier for tribute amount |
| Raid Strength Multiplier | 1.5x | Multiplier for raid points |

## File Structure

```
DebtCollector/
├── About/
│   └── About.xml
├── Assemblies/
│   └── DebtCollector.dll (compiled)
├── Defs/
│   ├── FactionDef/
│   │   └── DebtCollector_FactionDef.xml
│   └── IncidentDef/
│       └── DebtCollector_Incidents.xml
├── Languages/
│   └── English/
│       └── Keyed/
│           └── DebtCollector_Keyed.xml
├── Patches/
│   └── DebtCollector_Patches.xml
└── Source/
    └── DebtCollector/
        ├── DebtCollector.csproj
        ├── Core/
        ├── Comms/
        ├── DefOf/
        ├── Harmony/
        ├── Incidents/
        ├── Tests/
        └── World/
```

## Troubleshooting

### Settlement Not Spawning
- Check log for warnings about tile finding
- Use "Force Place Settlement" debug action
- Increase max settlement distance in settings

### Faction Not Found
- Ensure FactionDef XML is valid
- Check mod load order (should be after Core)

### Gizmos Not Appearing
- Verify CommsConsole is powered
- Check that patch XML is applying (no XML errors in log)
- Verify the faction exists

## License

MIT License - Feel free to use, modify, and distribute.

## Credits

- Ludeon Studios for RimWorld
- Pardeike for Harmony
