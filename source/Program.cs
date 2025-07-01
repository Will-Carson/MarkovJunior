// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Diagnostics;
using System.Collections.Generic;

static class Program
{
    static void Main(string[] args)
    {
        // Parse command line arguments
        var cliArgs = ParseArgs(args);
        
        if (cliArgs.ShowHelp)
        {
            ShowHelp();
            return;
        }

        if (cliArgs.HasCliArgs)
        {
            // CLI mode: run specific model
            RunSingleModel(cliArgs);
        }
        else
        {
            // Legacy mode: run all models from models.xml
            RunAllModels();
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine("MarkovJunior - Probabilistic Programming Language");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  MarkovJunior                           Run all models from models.xml (legacy mode)");
        Console.WriteLine("  MarkovJunior [options] <model-file>    Run specific model file");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <dir>     Output directory (default: output)");
        Console.WriteLine("  -s, --seed <number>    Random seed (default: random)");
        Console.WriteLine("  --steps <number>       Number of steps (default: 50000)");
        Console.WriteLine("  --amount <number>      Number of iterations (default: 2)");
        Console.WriteLine("  --pixelsize <number>   Pixel size for rendering (default: 4)");
        Console.WriteLine("  --gif                  Generate animated GIF");
        Console.WriteLine("  --iso                  Force isometric rendering for 3D");
        Console.WriteLine("  --stdout               Output grid as text to stdout (for programmatic use)");
        Console.WriteLine("  -h, --help             Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  MarkovJunior models/Basic.xml");
        Console.WriteLine("  MarkovJunior -o my_output --seed 12345 models/Basic.xml");
        Console.WriteLine("  MarkovJunior --gif --steps 1000 models/Basic.xml");
    }

    static CliArgs ParseArgs(string[] args)
    {
        var result = new CliArgs();
        
        if (args.Length == 0)
        {
            return result; // Legacy mode
        }

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    result.ShowHelp = true;
                    return result;
                
                case "-o":
                case "--output":
                    if (i + 1 < args.Length)
                    {
                        result.OutputDir = args[++i];
                        result.HasCliArgs = true;
                    }
                    break;
                
                case "-s":
                case "--seed":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int seed))
                    {
                        result.Seed = seed;
                        result.HasCliArgs = true;
                    }
                    break;
                
                case "--steps":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int steps))
                    {
                        result.Steps = steps;
                        result.HasCliArgs = true;
                    }
                    break;
                
                case "--amount":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int amount))
                    {
                        result.Amount = amount;
                        result.HasCliArgs = true;
                    }
                    break;
                
                case "--pixelsize":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int pixelsize))
                    {
                        result.PixelSize = pixelsize;
                        result.HasCliArgs = true;
                    }
                    break;
                
                case "--gif":
                    result.Gif = true;
                    result.HasCliArgs = true;
                    break;
                
                case "--iso":
                    result.Iso = true;
                    result.HasCliArgs = true;
                    break;
                
                case "--stdout":
                    result.Stdout = true;
                    result.HasCliArgs = true;
                    break;
                
                default:
                    if (!args[i].StartsWith("-"))
                    {
                        result.ModelFile = args[i];
                        result.HasCliArgs = true;
                    }
                    break;
            }
        }

        return result;
    }

    static void RunSingleModel(CliArgs args)
    {
        if (string.IsNullOrEmpty(args.ModelFile))
        {
            Console.WriteLine("ERROR: No model file specified");
            return;
        }

        if (!File.Exists(args.ModelFile))
        {
            Console.WriteLine($"ERROR: Model file not found: {args.ModelFile}");
            return;
        }

        Stopwatch sw = Stopwatch.StartNew();
        
        // Create output directory
        var outputDir = args.OutputDir ?? "output";
        var folder = Directory.CreateDirectory(outputDir);
        
        // Load palette
        Dictionary<char, int> palette;
        try
        {
            palette = XDocument.Load("resources/palette.xml").Root.Elements("color")
                .ToDictionary(x => x.Get<char>("symbol"), x => (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16));
        }
        catch (Exception e)
        {
            Console.WriteLine($"ERROR: Could not load palette: {e.Message}");
            return;
        }

        // Load model
        XDocument modeldoc;
        try 
        { 
            modeldoc = XDocument.Load(args.ModelFile, LoadOptions.SetLineInfo); 
        }
        catch (Exception e)
        {
            Console.WriteLine($"ERROR: couldn't open xml file {args.ModelFile}: {e.Message}");
            return;
        }

        // Extract model name from filename
        string modelName = Path.GetFileNameWithoutExtension(args.ModelFile);
        Console.Write($"{modelName} > ");

        // Get model dimensions (try to read from XML, use defaults if not found)
        var root = modeldoc.Root;
        int linearSize = root.Get("size", 60);
        int dimension = root.Get("d", 2);
        int MX = root.Get("length", linearSize);
        int MY = root.Get("width", linearSize);
        int MZ = root.Get("height", dimension == 2 ? 1 : linearSize);

        // Create interpreter
        Interpreter interpreter = Interpreter.Load(root, MX, MY, MZ);
        if (interpreter == null)
        {
            Console.WriteLine("ERROR: Failed to load interpreter");
            return;
        }

        // Apply CLI arguments or use defaults
        int amount = args.Amount ?? 2;
        int pixelsize = args.PixelSize ?? 4;
        bool gif = args.Gif;
        bool iso = args.Iso;
        bool stdout = args.Stdout;
        int steps = args.Steps ?? (gif ? 1000 : 50000);
        
        if (gif) amount = 1;

        // Handle custom palette from model
        Dictionary<char, int> customPalette = new(palette);
        foreach (var x in root.Elements("color")) 
            customPalette[x.Get<char>("symbol")] = (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16);

        // Run simulation
        Random random = new();
        for (int k = 0; k < amount; k++)
        {
            int seed = args.Seed ?? random.Next();
            foreach ((byte[] result, char[] legend, int FX, int FY, int FZ) in interpreter.Run(seed, steps, gif))
            {
                if (stdout)
                {
                    // Output grid as text to stdout for programmatic parsing
                    OutputGridToStdout(result, legend, FX, FY, FZ);
                }
                else
                {
                    // Normal file output
                    int[] colors = legend.Select(ch => customPalette[ch]).ToArray();
                    string outputname = gif ? $"{outputDir}/{interpreter.counter}" : $"{outputDir}/{modelName}_{seed}";
                    
                    if (FZ == 1 || iso)
                    {
                        var (bitmap, WIDTH, HEIGHT) = Graphics.Render(result, FX, FY, FZ, colors, pixelsize, 0);
                        Graphics.SaveBitmap(bitmap, WIDTH, HEIGHT, outputname + ".png");
                    }
                    else 
                    {
                        VoxHelper.SaveVox(result, (byte)FX, (byte)FY, (byte)FZ, colors, outputname + ".vox");
                    }
                }
            }
            Console.WriteLine("DONE");
        }
        
        Console.WriteLine($"time = {sw.ElapsedMilliseconds}ms");
    }

    static void OutputGridToStdout(byte[] result, char[] legend, int FX, int FY, int FZ)
    {
        // Output grid dimensions and legend for easy parsing
        Console.WriteLine($"GRID_START {FX} {FY} {FZ}");
        Console.WriteLine($"LEGEND {string.Join("", legend)}");
        
        // Output the grid data layer by layer
        for (int z = 0; z < FZ; z++)
        {
            Console.WriteLine($"LAYER {z}");
            for (int y = 0; y < FY; y++)
            {
                var row = new char[FX];
                for (int x = 0; x < FX; x++)
                {
                    int index = x + y * FX + z * FX * FY;
                    if (index < result.Length)
                    {
                        byte value = result[index];
                        row[x] = value < legend.Length ? legend[value] : ' ';
                    }
                    else
                    {
                        row[x] = ' ';
                    }
                }
                Console.WriteLine(new string(row));
            }
        }
        Console.WriteLine("GRID_END");
    }

    static void RunAllModels()
    {
        Stopwatch sw = Stopwatch.StartNew();
        var folder = System.IO.Directory.CreateDirectory("output");
        foreach (var file in folder.GetFiles()) file.Delete();

        Dictionary<char, int> palette = XDocument.Load("resources/palette.xml").Root.Elements("color").ToDictionary(x => x.Get<char>("symbol"), x => (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16));

        Random meta = new();
        XDocument xdoc = XDocument.Load("models.xml", LoadOptions.SetLineInfo);
        foreach (XElement xmodel in xdoc.Root.Elements("model"))
        {
            string name = xmodel.Get<string>("name");
            int linearSize = xmodel.Get("size", -1);
            int dimension = xmodel.Get("d", 2);
            int MX = xmodel.Get("length", linearSize);
            int MY = xmodel.Get("width", linearSize);
            int MZ = xmodel.Get("height", dimension == 2 ? 1 : linearSize);

            Console.Write($"{name} > ");
            string filename = $"models/{name}.xml";
            XDocument modeldoc;
            try { modeldoc = XDocument.Load(filename, LoadOptions.SetLineInfo); }
            catch (Exception)
            {
                Console.WriteLine($"ERROR: couldn't open xml file {filename}");
                continue;
            }

            Interpreter interpreter = Interpreter.Load(modeldoc.Root, MX, MY, MZ);
            if (interpreter == null)
            {
                Console.WriteLine("ERROR");
                continue;
            }

            int amount = xmodel.Get("amount", 2);
            int pixelsize = xmodel.Get("pixelsize", 4);
            string seedString = xmodel.Get<string>("seeds", null);
            int[] seeds = seedString?.Split(' ').Select(s => int.Parse(s)).ToArray();
            bool gif = xmodel.Get("gif", false);
            bool iso = xmodel.Get("iso", false);
            int steps = xmodel.Get("steps", gif ? 1000 : 50000);
            int gui = xmodel.Get("gui", 0);
            if (gif) amount = 1;

            Dictionary<char, int> customPalette = new(palette);
            foreach (var x in xmodel.Elements("color")) customPalette[x.Get<char>("symbol")] = (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16);

            for (int k = 0; k < amount; k++)
            {
                int seed = seeds != null && k < seeds.Length ? seeds[k] : meta.Next();
                foreach ((byte[] result, char[] legend, int FX, int FY, int FZ) in interpreter.Run(seed, steps, gif))
                {
                    int[] colors = legend.Select(ch => customPalette[ch]).ToArray();
                    string outputname = gif ? $"output/{interpreter.counter}" : $"output/{name}_{seed}";
                    if (FZ == 1 || iso)
                    {
                        var (bitmap, WIDTH, HEIGHT) = Graphics.Render(result, FX, FY, FZ, colors, pixelsize, gui);
                        if (gui > 0) GUI.Draw(name, interpreter.root, interpreter.current, bitmap, WIDTH, HEIGHT, customPalette);
                        Graphics.SaveBitmap(bitmap, WIDTH, HEIGHT, outputname + ".png");
                    }
                    else VoxHelper.SaveVox(result, (byte)FX, (byte)FY, (byte)FZ, colors, outputname + ".vox");
                }
                Console.WriteLine("DONE");
            }
        }
        Console.WriteLine($"time = {sw.ElapsedMilliseconds}");
    }

    class CliArgs
    {
        public bool ShowHelp { get; set; }
        public bool HasCliArgs { get; set; }
        public string ModelFile { get; set; }
        public string OutputDir { get; set; }
        public int? Seed { get; set; }
        public int? Steps { get; set; }
        public int? Amount { get; set; }
        public int? PixelSize { get; set; }
        public bool Gif { get; set; }
        public bool Iso { get; set; }
        public bool Stdout { get; set; }
    }
}
