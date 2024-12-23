# YARG_sACN_to_DMX

This program reads sACN data from YARG,
transforms it to match 2, 4, or 8 [DMX](https://en.wikipedia.org/wiki/DMX512) lights,
and then sends the resulting DMX universe
over an FTDI-based USB->DMX converter, such as this one:
https://www.amazon.com/gp/product/B07WV6P5W6

YARG's sACN data is conceptually based on the PDP Rock Band Stage Kit:
- inner circle of 8 green lights
- concentric circle of 8 blue ilghts
- concentric circle of 8 orange ilghts
- concentric circle of 8 red ilghts

And a separate strobe light.

This program translates that to these cheap DMX lights:
- OOPSK 36LEDs RGB Par Lights - 36W LED Par Can Lights
- https://www.amazon.com/gp/product/B0CJLD5QXY

Each light must be configured in 7-channel mode - displaying:
- A001, A008 (2-light mode)
- A001, A008, A015, A022 (4-light mode)
- A001, A008, A015, A022, A029, A036, A043, A051 (8-light mode)

DMX 7-channel mapping:
- 1: Master Dimmer
- 2: Red Dimmer
- 3: Green Dimmer
- 4: Blue Dimmer
- 5: RGB (always set to 0)
- 6: Strobe speed
- 7: Mode (always set to 0)

Right now I'm lazy, so the number of lights should be set in the Program.cs line below:
 
  static private int light_count = 4; // 2, 4, or 8 supported

This program remaps the 8 sets of Stage Kit LEDs per the following rules:
- Blue -> Blue, Red -> Red, Green -> Green (reducing 24 LEDs to the 3 component colors within 2, 4, or 8 PAR lights)
- Orange = red 255 + green 128
- If strobe is set: change all lights to white, and set all DMX strobe channels

## Dependencies

This program builds with Microsoft Visual Studio Community 2022.

This project relies on these GitHub projects:
- https://github.com/wberdowski/DMX.NET/tree/master/Dmx.Net
- https://github.com/HakanL/Haukcode.sACN

