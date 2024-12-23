# YARG_sACN_to_DMX

This program reads [sACN](https://store.chipkin.com/articles/streaming-architecture-for-control-networks-sacn) data from YARG,
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
- OOPSK 36LEDs RGB [PAR](https://hyliteledlighting.com/2020/05/12/br-vs-par-bulbs/) Lights - 36W LED PAR Can Lights
- https://www.amazon.com/gp/product/B0CJLD5QXY
- Note that each light is 36 x 1W LEDs - that means 12 blue, 12 red, and 12 green, where the LEDs of each color are all controlled as one - they are *not* individually addressable.  Each light is a cheap "wash" light designed to produce a single color.  I was going to get one of those ridiculous spinning laser things, but that's simply too much stimulation for my tired old retinas.

Each light must be configured in 7-channel mode, configured by buttons on the back, to display on the back panel:
- A001, A008 (2-light mode)
- A001, A008, A015, A022 (4-light mode)
- A001, A008, A015, A022, A029, A036, A043, A051 (8-light mode)

This is the DMX 7-channel mapping used by the OOPSK lights.  There is no standard for DMX channel definitions, so other lights may be different:
- 1: Master Dimmer
- 2: Red Dimmer
- 3: Green Dimmer
- 4: Blue Dimmer
- 5: RGB (always set to 0)
- 6: Strobe speed
- 7: Mode (always set to 0)

Right now I'm lazy, so the number of lights should be set in the Program.cs line below:
 ```
  static private int light_count = 4; // 2, 4, or 8 supported
```
This program remaps the 8 sets of Stage Kit LEDs per the following rules:
- Blue -> Blue, Red -> Red, Green -> Green (reducing 24 LEDs to the 3 component colors within 2, 4, or 8 PAR lights)
- Orange = red 255 + green 128
- If strobe is set: change all lights to white, and set all DMX strobe channels
- In 2-light mode:
  - Light 1 maps to stage kit LEDs 1, 3, 5, and 7
  - Light 2 maps to stage kit LEDs 2, 4, 6, and 8
  - This generally works out as a nice alternating pattern
- In 4-light mode:
  - Light 1 maps to stage kit LEDs 1 and 4
  - Light 2 maps to stage kit LEDs 2 and 5
  - Light 3 maps to stage kit LEDs 3 and 6
  - Light 4 maps to stage kit LEDs 4 and 7
  - This generally means marching left to right or right to left

## Dependencies

This program builds with Microsoft Visual Studio Community 2022.

This project relies on these GitHub projects:
- https://github.com/wberdowski/DMX.NET/tree/master/Dmx.Net
  - I made a separate fork of this, just so I could keep everything 64-bit and .NET 8.0
  - https://github.com/Nyxyxylyth/DMX.NET
- https://github.com/HakanL/Haukcode.sACN

