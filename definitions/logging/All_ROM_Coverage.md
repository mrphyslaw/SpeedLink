# Logger ROM coverage

All seven ROMs with local tuning definitions have a logger profile. Each profile uses its own identifier records, MAP calibration locations and code guards. The existing 38-channel baseline was checked separately in every image.

| ROM | Parameters | Requests per full scan | Excluded baseline parameters | Native vectors |
| --- | ---: | ---: | ---: | ---: |
| L38KEH000 | 38 | 34 | 0 | 298 |
| L39SED000 | 38 | 34 | 0 | 298 |
| L39SEE000 | 38 | 34 | 0 | 298 |
| L39SEF000 | 38 | 34 | 0 | 298 |
| L3M7EF000 | 35 | 31 | 3 | 277 |
| L35HEH000 | 36 | 32 | 2 | 284 |
| L33XEH000 | 35 | 31 | 3 | 277 |

The checks execute bounded original ROM routines with synthetic RAM. They do not establish real-ECU session acceptance, physical values or rates. All 258 included ROM/channel combinations still need ECU testing. No ROM image is included.

## Parameters excluded from the current profiles

An excluded channel is not offered or polled by this draft. A similar signal may exist through another interface; that has not been established.

L3M7EF000: Desired fuel rail pressure, Fuel injection amount, Ambient air temperature.

L35HEH000: Desired fuel rail pressure, Ambient air temperature.

L33XEH000: Desired fuel rail pressure, Ambient air temperature, Catalyst temperature.

L3M7EF000 exports pulsewidth directly from a different internal producer. Its wire scale remains 0.008 ms/count; its profile does not substitute pulsewidth for fuel injection amount. Older revisions also use a direct percentage source for primary fuel trim. Those paths were checked independently.

The selected ROM must match the live ECU calibration ID. Load the vehicle's current full backup so MAP/boost readings use the correct calibration. Unknown ROMs remain readable; their logger layout is not guessed.

See `All_ROM_Coverage.csv` for every included/excluded channel and `All_ROM_ECU_Checklist.csv` for the remaining live checks. The earlier `Coverage.md` and `ECU_Test_Checklist.csv` retain the original L38KEH000 research view.
