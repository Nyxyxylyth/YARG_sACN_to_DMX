# YARG_sACN_to_DMX

This program reads [sACN](https://store.chipkin.com/articles/streaming-architecture-for-control-networks-sacn) data from YARG,
transforms it to match 2, 4, or 8 [DMX](https://en.wikipedia.org/wiki/DMX512) lights,
and then sends the resulting DMX universe
over an FTDI-based USB->DMX converter, such as this one: [DSD TECH USB to DMX cable](https://www.amazon.com/gp/product/B07WV6P5W6)

YARG's sACN data is conceptually based on the PDP Rock Band Stage Kit:
- inner diamond of 8 blue lights
- concentric circle of 8 green lights
- outer circle of alternating red and orange lights

    ![](https://github.com/Nyxyxylyth/YARG_sACN_to_DMX/blob/master/stagekit.gif)

And a separate strobe light.

This program remaps the 8 sets of Stage Kit LEDs to 2, 4, or 8 cheap DMX [PAR](https://hyliteledlighting.com/2020/05/12/br-vs-par-bulbs/) lights, following these rules:
- Blue -> Blue, Red -> Red, Green -> Green (reducing 24 LEDs to the 3 component colors within 2, 4, or 8 PAR lights)
- Orange = red 255 + green 128
- If strobe is set: change all lights to white, and set all DMX strobe channels
- In 2-light mode:
  - Stage kit LEDs 1, 3, 5, and 7 drive DMX light 1
  - Stage kit LEDs 2, 4, 6, and 8 drive DMX light 2
  - This generally works out as a nice alternating pattern
- In 4-light mode:
  - Stage kit LEDs 1 and 4 drive DMX light 1
  - Stage kit LEDs 2 and 5 drive DMX light 2
  - Stage kit LEDs 3 and 6 drive DMX light 3
  - Stage kit LEDs 4 and 7 drive DMX light 4
  - This generally means marching left to right or right to left
  - [4-light demo on YouTube](https://www.youtube.com/watch?v=yCDondbEzHc)

These are the lights I used:
- OOPSK 36LEDs RGB PAR Lights - [36W LED PAR Can Lights](https://www.amazon.com/gp/product/B0CJLD5QXY)
- Note that each light is a "wash" light, designed to produce a single color.  There are 36 x 1W LEDs: that means 12 blue, 12 red, and 12 green, where the LEDs of each color are all controlled as one, and are *not* individually addressable. I was going to get one of those ridiculous spinning laser things, but that's simply too much stimulation for my tired old retinas.
- You'll also need a male-female cable to daisy-chain each light's DMX out to the next light's DMX in.  These can be stubby little cables like [3.2ft DMX cable](https://www.amazon.com/gp/product/B07D4FMQK4)
- Depending on cable length and number of lights, you may need a 120-ohm [DMX terminator](https://www.amazon.com/gp/product/B000PO1H94) plugged in to the last light's DMX out in the chain.

Each light must be configured in 7-channel mode, configured by buttons on the back, to display on the back panel:
- A001, A008 (2-light mode)
- A001, A008, A015, A022 (4-light mode)
- A001, A008, A015, A022, A029, A036, A043, A050 (8-light mode)

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

The low-rent USB->DMX adapter I used does not have optical isolation, so beware of [ESD](https://en.wikipedia.org/wiki/Electrostatic_discharge):
- Touch something tied to earth ground before touching the USB interface, lights, or DMX cables
- ESD is more likely in low-humidity environments - a humidifier can help
- Shuffling your feet with some combinations of footwear and floor coverings (e.g., shag carpet) is a particularly bad idea
- Power the computer and the lights from the same circuit if you can

## Dependencies

This C# program builds with [Microsoft Visual Studio Community 2022](https://visualstudio.microsoft.com/downloads/).

This project relies on these GitHub projects:
- https://github.com/wberdowski/DMX.NET/tree/master/Dmx.Net
  - I made a separate fork of this, just so I could keep everything 64-bit and .NET 8.0 at https://github.com/Nyxyxylyth/DMX.NET
- https://github.com/HakanL/Haukcode.sACN

