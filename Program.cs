﻿// This program reads sACN data from YARG,
// transforms it to match 2, 4, or 8 DMX outputs,
// and then sends the resulting DMX universe
// over an FTDI-based USB->DMX converter.
//
// This project relies on these GitHub projects:
// - https://github.com/wberdowski/DMX.NET/tree/master/Dmx.Net
// - https://github.com/HakanL/Haukcode.sACN
//
// YARG's sACN data is conceptually based on the PDP Rock Band Stage Kit:
// - inner circle of 8 green lights
// - concentric circle of 8 blue ilghts
// - concentric circle of 8 orange ilghts
// - concentric circle of 8 red ilghts
// And a separate strobe light.
//
// This program translates that to these cheap DMX lights:
// OOPSK 36LEDs RGB Par Lights - 36W LED Par Can Lights
// https://www.amazon.com/gp/product/B0CJLD5QXY
//
// Each light must be configured in 7-channel mode - displaying:
// A001, A008 (2-light mode)
// A001, A008, A015, A022 (4-light mode)
// A001, A008, A015, A022, A029, A036, A043, A051 (8-light mode)
// 
// Right now I'm lazy, so the number of lights should be set in the line below:
//   static private int light_count = 4;
//
// This program remaps the 8 sets of Stage Kit LEDs per the following rules:
// - Blue -> Blue, Red -> Red, Green -> Green
// - Orange = red + green (okay, sure, it's yellow)
// - If strobe is set: change all lights to white,
//     and set all DMX strobe channels

/*
MIT License

Copyright (c) 2024 Neal Manson

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/


using Dmx.Net.Common;
using Dmx.Net.Controllers;
using Haukcode.sACN.Model;
using System.Buffers;
using System.Reactive.Linq;
using System.Net;
using System.Threading.Channels;
using System.Drawing;
using System.Reflection.Metadata;


namespace Haukcode.sACN;

public class Program
{
    private static readonly Guid acnSourceId = new Guid("{B32625A6-C280-4389-BD25-E0D13F5B50E0}");
    private static readonly string acnSourceName = "DMXPlayer";

    private static MemoryPool<byte> memoryPool = MemoryPool<byte>.Shared;
    private static double last = 0;

    private static byte[,] stageKitLeds = new byte[8, 4];

    private static int manualOverrideLight;
    enum OverrideControl
    {
        Override_none,
        Override_Red,
        Override_Green,
        Override_Blue,
        Override_Strobe
    }

    private static int manualOverrideControl;

    private static DmxTimer dmxTimer = new DmxTimer();
    private static OpenDmxController dmxController = new OpenDmxController(dmxTimer);

    private static Semaphore consoleSemaphore = new Semaphore(1,1);


    static private Color[] light = {
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0)
                                    };
    private const int LIGHT_LIMIT = 8;
    static private byte strobe = 0;
    static private int light_count = LIGHT_LIMIT;
    static private byte master_dimming = 255;
    static private ConsoleKey manualKey = ConsoleKey.None;

    public static void Main(string[] args)
    {
        Listen();
    }

    static void Listen()
    {
        Console.Clear();
        Console.CursorVisible = false;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("\x1B[1;12r"); // Set top 11 rows for scrolling area
        Console.SetCursorPosition(0, 11);

        var recvClient = new SACNClient(
            senderId: acnSourceId,
            senderName: acnSourceName,
            localAddress: IPAddress.Any);

        consoleSemaphore.WaitOne();

            recvClient.OnError.Subscribe(e =>
        {
            Console.WriteLine($"Error! {e.Message}");
        });

        //listener.OnReceiveRaw.Subscribe(d =>
        //{
        //    Console.WriteLine($"Received {d.Data.Length} bytes from {d.Host}");
        //});

        //recvClient.OnPacket.Subscribe(d =>
        //{
        //    Listener_OnPacket(d.TimestampMS, d.TimestampMS - last, d.Packet);
        //    last = d.TimestampMS;
        //});

        var channel = Channel.CreateUnbounded<ReceiveDataPacket>();

        // Not sure about the transform here, the packet may use memory from
        // the memory pool and it may not be safe to pass it around like this
        recvClient.StartRecordPipeline(p => WritePacket(channel, p), () => channel.Writer.Complete());

        var writerTask = Task.Factory.StartNew(async () =>
        {
            await WriteToDiskAsync(channel, CancellationToken.None);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

        recvClient.JoinDMXUniverse(1);
//        recvClient.JoinDMXUniverse(2);  // one is plenty for YARG

        Console.WriteLine("Starting...");

        // Try to connect to an Open DMX interface
        try
        {
            dmxController.Open(0);
        }
        catch ( Exception e )
        {
            // try again later
        }


        // Don't forget to start the timer.
        dmxTimer.Start();

        Console.SetCursorPosition(0, 18);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Stage Kit Data: 01234567");
        Console.SetCursorPosition(14, 19); Console.ForegroundColor = ConsoleColor.Red;        Console.Write("R");
        Console.SetCursorPosition(14, 20); Console.ForegroundColor = ConsoleColor.Green;      Console.Write("G");
        Console.SetCursorPosition(14, 21); Console.ForegroundColor = ConsoleColor.DarkCyan;   Console.Write("B");
        Console.SetCursorPosition(14, 22); Console.ForegroundColor = ConsoleColor.DarkYellow; Console.Write("O");

        Console.SetCursorPosition(30, 18);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("DMX Output");
        Console.SetCursorPosition(30, 27);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("press L to toggle 2/4/8 lights");
        Console.SetCursorPosition(30, 28);
        Console.Write("press M to toggle master dimming: 255");
        Console.SetCursorPosition(30, 29);
        Console.Write("press 0-7 for manual control: off");

        Console.SetCursorPosition(0, 11);
        Console.ForegroundColor = ConsoleColor.Gray;

        consoleSemaphore.Release();

        for (; ; )
        {
            // If the USB->DMX controller gets unplugged, try to reconnect
            if (dmxController.IsOpen == false)
            {
                try
                {
                    dmxController.Open(0);
                }
                catch (Exception e)
                {
                    // try again later
                }
            }
            else
            {
                if( manualKey != ConsoleKey.None )
                {
                    // for manual control, zero all lights
                    Color[] manualLight = {
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0),
                                        Color.FromArgb(0, 0, 0)
                                    };

                    // then set the selected one to blue
                    manualLight[manualKey - ConsoleKey.D0] = Color.FromArgb(0, 0, 255);
                    dmxController.SetChannelRange(1,
                                                master_dimming, manualLight[0].R, manualLight[0].G, manualLight[0].B, 0, strobe, 0,
                                                master_dimming, manualLight[1].R, manualLight[1].G, manualLight[1].B, 0, strobe, 0,
                                                master_dimming, manualLight[2].R, manualLight[2].G, manualLight[2].B, 0, strobe, 0,
                                                master_dimming, manualLight[3].R, manualLight[3].G, manualLight[3].B, 0, strobe, 0,
                                                master_dimming, manualLight[4].R, manualLight[4].G, manualLight[4].B, 0, strobe, 0,
                                                master_dimming, manualLight[5].R, manualLight[5].G, manualLight[5].B, 0, strobe, 0,
                                                master_dimming, manualLight[6].R, manualLight[6].G, manualLight[6].B, 0, strobe, 0,
                                                master_dimming, manualLight[7].R, manualLight[7].G, manualLight[7].B, 0, strobe, 0
                                                );
                }
                else
                {
                    // While USB-DMX is plugged in, update the DMX outputs
                    switch (light_count)
                    {
                        // Totally inelegant, but I'm still experimenting
                        case 2:
                            dmxController.SetChannelRange(1,
                                                        master_dimming, light[0].R, light[0].G, light[0].B, 0, strobe, 0,
                                                        master_dimming, light[1].R, light[1].G, light[1].B, 0, strobe, 0
                                                        );
                            break;

                        case 4:
                            dmxController.SetChannelRange(1,
                                                        master_dimming, light[0].R, light[0].G, light[0].B, 0, strobe, 0,
                                                        master_dimming, light[1].R, light[1].G, light[1].B, 0, strobe, 0,
                                                        master_dimming, light[2].R, light[2].G, light[2].B, 0, strobe, 0,
                                                        master_dimming, light[3].R, light[3].G, light[3].B, 0, strobe, 0
                                                        );
                            break;

                        case LIGHT_LIMIT:
                            dmxController.SetChannelRange(1,
                                                        master_dimming, light[0].R, light[0].G, light[0].B, 0, strobe, 0,
                                                        master_dimming, light[1].R, light[1].G, light[1].B, 0, strobe, 0,
                                                        master_dimming, light[2].R, light[2].G, light[2].B, 0, strobe, 0,
                                                        master_dimming, light[3].R, light[3].G, light[3].B, 0, strobe, 0,
                                                        master_dimming, light[4].R, light[4].G, light[4].B, 0, strobe, 0,
                                                        master_dimming, light[5].R, light[5].G, light[5].B, 0, strobe, 0,
                                                        master_dimming, light[6].R, light[6].G, light[6].B, 0, strobe, 0,
                                                        master_dimming, light[7].R, light[7].G, light[7].B, 0, strobe, 0
                                                        );
                            break;
                    }
                }

                // Make sure the main loop here isn't fighting with the YARG receive thread over the console
                if (consoleSemaphore.WaitOne() == true)
                {
                    // Update the table of lights (0..7) and RGB outputs
                    for (int i = 0; i < light_count; i++)
                    {
                        Console.SetCursorPosition(30, 19 + i);
                        Console.Write("\x1B[37mLight {0}: \x1B[31m{1:000} \x1B[32m{2:000} \x1B[36m{3:000}\u001b[37m      ", i, light[i].R, light[i].G, light[i].B);
                    }

                    // Update stage kit LED indicators
                    for (int j = 0; j < 4; j++)
                    {
                        Console.SetCursorPosition(16, 19 + j);
                        switch (j)
                        {
                            case 0: Console.ForegroundColor = ConsoleColor.Red; break;
                            case 1: Console.ForegroundColor = ConsoleColor.Green; break;
                            case 2: Console.ForegroundColor = ConsoleColor.Blue; break;
                            case 3: Console.ForegroundColor = ConsoleColor.DarkYellow; break;
                        }
                        for (int i = 0; i < LIGHT_LIMIT; i++)
                        {
                            if (stageKitLeds[i, j] == 0)
                                Console.Write(" ");
                            else
                                Console.Write("*");
                        }
                    }

                }


                Console.ForegroundColor = ConsoleColor.Gray;

                // Update DMX connection status
                Console.SetCursorPosition(42, 18);
                if (dmxController.IsOpen == true)
                {
                    Console.Write("connected   ");
                }
                else
                {
                    Console.Write("disconnected");
                }


                // Handle pressing L key to toggle between number of lights (2/4/8)
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo keyPressed;
                    keyPressed = Console.ReadKey(true);
                    if (keyPressed.Key == ConsoleKey.L)
                    {
                        // Advance light count
                        switch (light_count)
                        {
                            case 2: light_count = 4; break;
                            case 4: light_count = LIGHT_LIMIT; break;
                            case LIGHT_LIMIT:
                                light_count = 2;
                                // Erase the remaining lines
                                for (int i = 2; i < LIGHT_LIMIT; i++)
                                {
                                    Console.SetCursorPosition(30, 19 + i);
                                    Console.Write("                    ");
                                }
                                break;
                        }
                    }
                    else if( keyPressed.Key == ConsoleKey.M )
                    {
                        if (master_dimming > 15)
                            master_dimming -= 16;
                        else
                            master_dimming = 255;

                        Console.SetCursorPosition(30, 28);
                        Console.Write("press M to toggle master dimming: " + master_dimming + "    ");
                    }
                    else
                    {
                        if( (keyPressed.Key >= ConsoleKey.D0) && (keyPressed.Key <= ConsoleKey.D7) )
                        {
                            if (manualKey == keyPressed.Key)
                            {
                                manualKey = ConsoleKey.None;
                                Console.SetCursorPosition(30, 29);
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.Write("press 0-7 for manual control: off       ");
                            }
                            else
                            {
                                manualKey = keyPressed.Key;
                                Console.SetCursorPosition(30, 29);
                                Console.ForegroundColor = ConsoleColor.White;
                                Console.Write("press 0-7 for manual control: Light " + char.ToString((char)('0' + (manualKey - ConsoleKey.D0))));
                            }
                        }
                    }
                }

                Console.SetCursorPosition(0, 11);

                consoleSemaphore.Release();
            }

            //Thread.Sleep(10);  // shoot for 60 Hz
        }

        // Cleanup, ensure all channels are set to 0
        dmxTimer.Dispose();
        dmxController.Dispose();
    }

    private static async Task WritePacket(Channel<ReceiveDataPacket> channel, ReceiveDataPacket receiveData)
    {
        var dmxData = TransformPacket(receiveData);

        if (dmxData == null)
            return;

        await channel.Writer.WriteAsync(dmxData, CancellationToken.None);
    }

    private static ReceiveDataPacket TransformPacket(ReceiveDataPacket receiveData)
    {
        var framingLayer = receiveData.Packet.RootLayer?.FramingLayer;
        if (framingLayer == null)
            return null;

        switch (framingLayer)
        {
            case DataFramingLayer dataFramingLayer:
                var dmpLayer = dataFramingLayer.DMPLayer;

                if (dmpLayer == null || dmpLayer.Length < 1)
                    // Unknown/unsupported
                    return null;

                if (dmpLayer.StartCode != 0)
                    // We only support start code 0
                    return null;

                // Hack
                var newBuf = new byte[dmpLayer.Data.Length];
                dmpLayer.Data.CopyTo(newBuf);
                dmpLayer.Data = newBuf;

                return receiveData;

            case SyncFramingLayer syncFramingLayer:
                return receiveData;
        }

        return null;
    }

    private static async Task WriteToDiskAsync(Channel<ReceiveDataPacket> inputChannel, CancellationToken cancellationToken)
    {
        await foreach (var dmxData in inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            Listener_OnPacket(dmxData.TimestampMS, dmxData.TimestampMS - last, dmxData);
            last = dmxData.TimestampMS;
        }
    }

    private static void Listener_OnPacket(double timestampMS, double sinceLast, ReceiveDataPacket e)
    {
        var dataPacket = e.Packet.RootLayer.FramingLayer as DataFramingLayer;

        if (dataPacket == null)
            return;

        if (consoleSemaphore.WaitOne(0) == true)
        {
            Console.SetCursorPosition(0, 13);
            //Console.WriteLine($"+{sinceLast:N2}              ");
            Console.WriteLine($"Packet from {dataPacket.SourceName}\tu{dataPacket.UniverseId}\ts{dataPacket.SequenceId}            ");
            //Console.WriteLine($"Data {string.Join(",", dataPacket.DMPLayer.Data.ToArray().Take(64))}...");
            Console.SetCursorPosition(0, 11);
            consoleSemaphore.Release();
        }

        Array YARGdmxData;
        YARGdmxData = (Array)dataPacket.DMPLayer.Data.ToArray();

        for (int i = 0; i < LIGHT_LIMIT; i++)
        {
            for (int j = 0; j<4; j++)
            {
                stageKitLeds[i, j] = (byte) YARGdmxData.GetValue(i * 8 + 1 + j);
            }
        }

        switch (light_count)
        {
            case LIGHT_LIMIT: 
                map_lights8(YARGdmxData);
                break;

            case 4: 
                map_lights4(YARGdmxData);
                break;

            case 2:
            default:
                map_lights2(YARGdmxData);
                break;

        }
    }

    private static void map_lights2(Array dmxData )
    {
        int[] red = { 0, 0 };
        int[] blue = { 0, 0 };
        int[] green = { 0, 0 };

        // Inelegant mapping of YARG default DMX channels to DMX PAR colors
        if (((byte)dmxData.GetValue(1) != 0) ||
            ((byte)dmxData.GetValue(17) != 0) ||
            ((byte)dmxData.GetValue(33) != 0) ||
            ((byte)dmxData.GetValue(49) != 0) ||
            ((byte)dmxData.GetValue(4) != 0) ||
            ((byte)dmxData.GetValue(20) != 0) ||
            ((byte)dmxData.GetValue(36) != 0) ||
            ((byte)dmxData.GetValue(52) != 0))
        {
            red[0] = 255;
        }

        if (((byte)dmxData.GetValue(9) != 0) ||
            ((byte)dmxData.GetValue(25) != 0) ||
            ((byte)dmxData.GetValue(41) != 0) ||
            ((byte)dmxData.GetValue(57) != 0) ||
            ((byte)dmxData.GetValue(12) != 0) ||
            ((byte)dmxData.GetValue(28) != 0) ||
            ((byte)dmxData.GetValue(44) != 0) ||
            ((byte)dmxData.GetValue(60) != 0))
        {
            red[1] = 255;
        }

        if (((byte)dmxData.GetValue(3) != 0) ||
            ((byte)dmxData.GetValue(19) != 0) ||
            ((byte)dmxData.GetValue(35) != 0) ||
            ((byte)dmxData.GetValue(51) != 0))
        {
            blue[0] = 255;
        }
        if (((byte)dmxData.GetValue(11) != 0) ||
            ((byte)dmxData.GetValue(27) != 0) ||
            ((byte)dmxData.GetValue(43) != 0) ||
            ((byte)dmxData.GetValue(59) != 0))
        {
            blue[1] = 255;
        }

        if (((byte)dmxData.GetValue(2) != 0) ||
            ((byte)dmxData.GetValue(18) != 0) ||
            ((byte)dmxData.GetValue(34) != 0) ||
            ((byte)dmxData.GetValue(50) != 0) )
        {
            green[0] = 255;
        }
        else if (((byte)dmxData.GetValue(4) != 0) ||
            ((byte)dmxData.GetValue(20) != 0) ||
            ((byte)dmxData.GetValue(36) != 0) ||
            ((byte)dmxData.GetValue(52) != 0))
        {
            green[0] = 128;
        }

        if (((byte)dmxData.GetValue(10) != 0) ||
            ((byte)dmxData.GetValue(26) != 0) ||
            ((byte)dmxData.GetValue(42) != 0) ||
            ((byte)dmxData.GetValue(58) != 0) )
        {
            green[1] = 255;
        }
        else if(((byte)dmxData.GetValue(12) != 0) ||
                ((byte)dmxData.GetValue(28) != 0) ||
                ((byte)dmxData.GetValue(44) != 0) ||
                ((byte)dmxData.GetValue(60) != 0))
        {
            green[1] = 128;
        }

        // strobe: 7 15 23 31 39 47 55 63 (-1)
        if ((byte)dmxData.GetValue(6) == 0)
        {
            strobe = 0;
            for (int i = 0; i < 2; i++)
            {
                light[i] = Color.FromArgb(red[i], green[i], blue[i]);
            }
        }
        else if ((byte)dmxData.GetValue(6) == 64)
        {
            // strobe slow
            strobe = 220;
            for (int i = 0; i < 2; i++)
            {
                light[i] = Color.FromArgb(255, 255, 255);
            }
        }
        else if ((byte)dmxData.GetValue(6) == 191)
        {
            // strobe slow
            strobe = 240;
            for (int i = 0; i < 2; i++)
            {
                light[i] = Color.FromArgb(255, 255, 255);
            }
        }
    }

    private static void map_lights4(Array dmxData)
    {
        int[] red = { 0, 0, 0, 0 };
        int[] blue = { 0, 0, 0, 0 };
        int[] green = { 0, 0, 0, 0 };

        // Inelegant mapping of YARG default DMX channels to DMX PAR colors
        if (((byte)dmxData.GetValue(1) != 0) ||
            ((byte)dmxData.GetValue(33) != 0) ||
            ((byte)dmxData.GetValue(4) != 0) ||
            ((byte)dmxData.GetValue(36) != 0))
        {
            red[0] = 255;
        }

        if (((byte)dmxData.GetValue(9) != 0) ||
            ((byte)dmxData.GetValue(41) != 0) ||
            ((byte)dmxData.GetValue(12) != 0) ||
            ((byte)dmxData.GetValue(44) != 0))
        {
            red[1] = 255;
        }

        if (((byte)dmxData.GetValue(17) != 0) ||
            ((byte)dmxData.GetValue(49) != 0) ||
            ((byte)dmxData.GetValue(20) != 0) ||
            ((byte)dmxData.GetValue(52) != 0))
        {
            red[2] = 255;
        }

        if (((byte)dmxData.GetValue(25) != 0) ||
            ((byte)dmxData.GetValue(57) != 0) ||
            ((byte)dmxData.GetValue(28) != 0) ||
            ((byte)dmxData.GetValue(60) != 0))
        {
            red[3] = 255;
        }

        if (((byte)dmxData.GetValue(3) != 0) ||
            ((byte)dmxData.GetValue(35) != 0))
        {
            blue[0] = 255;
        }
        if (((byte)dmxData.GetValue(11) != 0) ||
            ((byte)dmxData.GetValue(43) != 0))
        {
            blue[1] = 255;
        }

        if (((byte)dmxData.GetValue(19) != 0) ||
            ((byte)dmxData.GetValue(51) != 0))
        {
            blue[2] = 255;
        }
        if (((byte)dmxData.GetValue(27) != 0) ||
            ((byte)dmxData.GetValue(59) != 0))
        {
            blue[3] = 255;
        }

        if (((byte)dmxData.GetValue(2) != 0) ||
            ((byte)dmxData.GetValue(34) != 0) )
        {
            green[0] = 255;
        }
        else if (((byte)dmxData.GetValue(4) != 0) ||
            ((byte)dmxData.GetValue(36) != 0))
        {
            green[0] = 128;
        }

        if (((byte)dmxData.GetValue(10) != 0) ||
            ((byte)dmxData.GetValue(42) != 0))
        {
            green[1] = 255;
        }
        else if (((byte)dmxData.GetValue(12) != 0) ||
                 ((byte)dmxData.GetValue(44) != 0))
        {
            green[1] = 128;
        }

        if (((byte)dmxData.GetValue(18) != 0) ||
            ((byte)dmxData.GetValue(50) != 0) )
        {
            green[2] = 255;
        }
        else if (((byte)dmxData.GetValue(20) != 0) ||
            ((byte)dmxData.GetValue(52) != 0))
        {
            green[2] = 128;
        }

        if (((byte)dmxData.GetValue(26) != 0) ||
            ((byte)dmxData.GetValue(58) != 0))
        {
            green[3] = 255;
        }
        else if (((byte)dmxData.GetValue(28) != 0) ||
            ((byte)dmxData.GetValue(60) != 0))
        {
            green[3] = 128;
        }

        // strobe: 7 15 23 31 39 47 55 63 (-1)
        if ((byte)dmxData.GetValue(6) == 0)
        {
            strobe = 0;
            for (int i = 0; i < 4; i++)
            {
                light[i] = Color.FromArgb(red[i], green[i], blue[i]);
            }
        }
        else if ((byte)dmxData.GetValue(6) == 64)
        {
            // strobe slow
            strobe = 220;
            for (int i = 0; i < 4; i++)
            {
                light[i] = Color.FromArgb(255, 255, 255);
            }
        }
        else if ((byte)dmxData.GetValue(6) == 191)
        {
            // strobe slow
            strobe = 240;
            for (int i = 0; i < 4; i++)
            {
                light[i] = Color.FromArgb(255, 255, 255);
            }
        }
    }

    private static void map_lights8( Array dmxData)
    {
        int[] red = { 0, 0, 0, 0, 0, 0, 0, 0 };
        int[] blue = { 0, 0, 0, 0, 0, 0, 0, 0 };
        int[] green = { 0, 0, 0, 0, 0, 0, 0, 0 };

        // Inelegant mapping of YARG default DMX channels to DMX PAR colors
        if (((byte)dmxData.GetValue(1) != 0) ||
            ((byte)dmxData.GetValue(4) != 0))
        {
            red[0] = 255;
        }

        if (((byte)dmxData.GetValue(9) != 0) ||
            ((byte)dmxData.GetValue(12) != 0))
        {
            red[1] = 255;
        }

        if (((byte)dmxData.GetValue(17) != 0) ||
            ((byte)dmxData.GetValue(20) != 0))
        {
            red[2] = 255;
        }

        if (((byte)dmxData.GetValue(25) != 0) ||
            ((byte)dmxData.GetValue(28) != 0))
        {
            red[3] = 255;
        }

        if (((byte)dmxData.GetValue(33) != 0) ||
            ((byte)dmxData.GetValue(36) != 0))
        {
            red[4] = 255;
        }

        if (((byte)dmxData.GetValue(41) != 0) ||
            ((byte)dmxData.GetValue(44) != 0))
        {
            red[5] = 255;
        }

        if (((byte)dmxData.GetValue(49) != 0) ||
            ((byte)dmxData.GetValue(52) != 0))
        {
            red[6] = 255;
        }

        if (((byte)dmxData.GetValue(57) != 0) ||
            ((byte)dmxData.GetValue(60) != 0))
        {
            red[7] = 255;
        }

        if (((byte)dmxData.GetValue(3) != 0))
        {
            blue[0] = 255;
        }
        if (((byte)dmxData.GetValue(11) != 0))
        {
            blue[1] = 255;
        }

        if (((byte)dmxData.GetValue(19) != 0))
        {
            blue[2] = 255;
        }
        if (((byte)dmxData.GetValue(27) != 0))
        {
            blue[3] = 255;
        }

        if (((byte)dmxData.GetValue(35) != 0))
        {
            blue[4] = 255;
        }
        if (((byte)dmxData.GetValue(43) != 0))
        {
            blue[5] = 255;
        }

        if (((byte)dmxData.GetValue(51) != 0))
        {
            blue[6] = 255;
        }
        if (((byte)dmxData.GetValue(59) != 0))
        {
            blue[7] = 255;
        }

        if (((byte)dmxData.GetValue(2) != 0) )
        {
            green[0] = 255;
        }

        if (((byte)dmxData.GetValue(10) != 0) )
        {
            green[1] = 255;
        }

        if (((byte)dmxData.GetValue(18) != 0) )
        {
            green[2] = 255;
        }

        if (((byte)dmxData.GetValue(26) != 0) )
        {
            green[3] = 255;
        }

        if (((byte)dmxData.GetValue(34) != 0) )
        {
            green[4] = 255;
        }

        if (((byte)dmxData.GetValue(42) != 0) )
        {
            green[5] = 255;
        }

        if (((byte)dmxData.GetValue(50) != 0) )
        {
            green[6] = 255;
        }

        if (((byte)dmxData.GetValue(58) != 0) )
        {
            green[7] = 255;
        }

        if (((byte)dmxData.GetValue(4) != 0))
        {
            green[0] = 128;
        }

        if (((byte)dmxData.GetValue(12) != 0))
        {
            green[1] = 128;
        }

        if (((byte)dmxData.GetValue(20) != 0))
        {
            green[2] = 128;
        }

        if (((byte)dmxData.GetValue(28) != 0))
        {
            green[3] = 128;
        }

        if (((byte)dmxData.GetValue(36) != 0))
        {
            green[4] = 128;
        }

        if (((byte)dmxData.GetValue(44) != 0))
        {
            green[5] = 128;
        }

        if (((byte)dmxData.GetValue(52) != 0))
        {
            green[6] = 128;
        }

        if (((byte)dmxData.GetValue(60) != 0))
        {
            green[7] = 128;
        }

        // strobe: 7 15 23 31 39 47 55 63 (-1)
        if ((byte)dmxData.GetValue(6) == 0)
        {
            strobe = 0;
            for (int i = 0; i < LIGHT_LIMIT; i++)
            {
                light[i] = Color.FromArgb(red[i], green[i], blue[i]);
            }
        }
        else if ((byte)dmxData.GetValue(6) == 64)
        {
            // strobe slow
            strobe = 220;
            for (int i = 0; i < LIGHT_LIMIT; i++)
            {
                light[i] = Color.FromArgb(255, 255, 255);
            }
        }
        else if ((byte)dmxData.GetValue(6) == 191)
        {
            // strobe slow
            strobe = 240;
            for (int i = 0; i < LIGHT_LIMIT; i++)
            {
                light[i] = Color.FromArgb(255, 255, 255);
            }
        }
    }
}
