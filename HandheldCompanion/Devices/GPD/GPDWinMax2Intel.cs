﻿using System.Collections.Generic;
using System.Numerics;

namespace HandheldCompanion.Devices;

public class GPDWinMax2Intel : GPDWinMax2
{
    public GPDWinMax2Intel()
    {
        // https://ark.intel.com/content/www/us/en/ark/products/226254/intel-core-i71260p-processor-18m-cache-up-to-4-70-ghz.html
        nTDP = new double[] { 15, 15, 20 };
        cTDP = new double[] { 15, 28 };
        GfxClock = new double[] { 100, 1400 };
        CpuClock = 4700;

        GyrometerAxis = new Vector3(1.0f, 1.0f, 1.0f);
        GyrometerAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'X' },
            { 'Y', 'Z' },
            { 'Z', 'Y' }
        };

        AccelerometerAxis = new Vector3(-1.0f, -1.0f, 1.0f);
        AccelerometerAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'X' },
            { 'Y', 'Z' },
            { 'Z', 'Y' }
        };
    }
}