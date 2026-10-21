# L38KEH000 logger coverage

38 channels, 34 distinct read identifiers, 36 queried channels and 2 calculated channels.

| Parameter | Identifier(s) | Canonical unit | ROM work | Decoder | ECU |
|---|---|---|---|---|---|
| Boost air temperature | 1723 | degC | Native | Passed | Pending |
| Boost pressure | 0900 + 0033 | kPa | Calculated | Passed | Pending |
| Intake manifold absolute pressure | 0900 | kPa | Native | Passed | Pending |
| MAP sensor voltage | 0900 | V | Native | Passed | Pending |
| Wastegate control solenoid duty cycle | 16E1 | percent | Native | Passed | Pending |
| Actual equivalence/air to fuel ratio | 0034 | lambda | Native | Passed | Pending |
| Desired equivalence/air to fuel ratio | 0044 | lambda | Native | Passed | Pending |
| Desired fuel rail pressure | 1747 | kPa | Native | Passed | Pending |
| Fuel injection amount | 1752 | mg | Native | Passed | Pending |
| Fuel injection pulse width | 1410 | ms | Native | Passed | Pending |
| Fuel pressure sensor voltage | 168B | V | Native | Passed | Pending |
| Fuel rail pressure | 0023 | kPa | Native | Passed | Pending |
| Injector duty cycle | 1410 + 000C | percent | Calculated | Passed | Pending |
| Long term fuel trim | 0007 | percent | Native | Passed | Pending |
| Narrow band oxygen sensor voltage | 0015 | V | Native | Passed | Pending |
| Short term fuel trim (primary sensor) | 0006 | percent | Native | Passed | Pending |
| Short term fuel trim (secondary sensor) | 163E | percent | Native | Passed | Pending |
| Absolute load | 0043 | ratio | Native | Passed | Pending |
| Accelerator pedal position | 1340 | percent | Native | Passed | Pending |
| Actual valve timing | 16CD | deg | Native | Passed | Pending |
| Ambient air temperature | 0046 | degC | Native | Passed | Pending |
| Barometric pressure | 0033 | kPa | Native | Passed | Pending |
| Catalyst temperature | 003C | degC | Native | Passed | Pending |
| Clutch pedal position | A211 | state | Native | Passed | Pending |
| Control module voltage | 0042 | V | Native | Passed | Pending |
| Desired engine RPM | 096E | rpm | Native | Passed | Pending |
| Desired throttle position | 004C | percent | Native | Passed | Pending |
| Engine coolant temperature | 0005 | degC | Native | Passed | Pending |
| Engine RPM | 000C | rpm | Native | Passed | Pending |
| Fan duty cycle | 091F | percent | Native | Passed | Pending |
| Intake air temperature | 000F | degC | Native | Passed | Pending |
| Mass airflow | 0010 | g/s | Native | Passed | Pending |
| Mass airflow sensor voltage | 1177 | V | Native | Passed | Pending |
| Neutral gear status | A211 | state | Native | Passed | Pending |
| Relative throttle position | 0045 | percent | Native | Passed | Pending |
| Vehicle speed | 000D | km/h | Native | Passed | Pending |
| Ignition timing advance | 000E | deg | Native | Passed | Pending |
| Knock retard | 1746 | deg | Native | Passed | Pending |

Native means bounded original encoder/descriptor instructions were checked with supplied RAM. Calculated channels use verified query inputs. Adapter logging, normal-session acceptance, physical readings and rates remain untested.
