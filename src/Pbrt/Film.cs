// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/film.h and film.cpp
//
// PhysLight code contributed by Anders Langlands and Luca Fascione
// Copyright (c) 2020, Weta Digital, Ltd.
// SPDX-License-Identifier: Apache-2.0
//
// The films' Pixel structs use inline arrays for pbrt's double[3] and
// AtomicDouble[3] members. SpectralFilm keeps its per-pixel bucket arrays
// in three film-wide arrays indexed by pixel instead of storing pointers
// in each Pixel.

using System.Runtime.CompilerServices;

namespace Pbrt;

[InlineArray(3)]
internal struct Double3
{
    private double e0;
}

[InlineArray(3)]
internal struct AtomicDouble3
{
    private AtomicDouble e0;
}

[InlineArray(3)]
internal struct VarianceEstimator3
{
    private VarianceEstimator<Float> e0;
}

// PixelSensor Definition
public sealed class PixelSensor
{
    // PixelSensor Public Members
    public SquareMatrix3 XYZFromSensorRGB = new();

    // PixelSensor Private Members
    private readonly DenselySampledSpectrum r_bar, g_bar, b_bar;
    private readonly Float imagingRatio;
    private const int nSwatchReflectances = 24;

    // PixelSensor Public Methods
    public PixelSensor(Spectrum r, Spectrum g, Spectrum b, RGBColorSpace outputColorSpace, Spectrum? sensorIllum,
                       Float imagingRatio)
    {
        r_bar = new DenselySampledSpectrum(r);
        g_bar = new DenselySampledSpectrum(g);
        b_bar = new DenselySampledSpectrum(b);
        this.imagingRatio = imagingRatio;
        // Compute XYZ from camera RGB matrix
        // Compute _rgbCamera_ values for training swatches
        Float[,] rgbCamera = new Float[nSwatchReflectances, 3];
        for (int i = 0; i < nSwatchReflectances; ++i)
        {
            RGB rgb = ProjectReflectanceRGB(swatchReflectances[i], sensorIllum!, r_bar, g_bar, b_bar);
            for (int c = 0; c < 3; ++c)
                rgbCamera[i, c] = rgb[c];
        }

        // Compute _xyzOutput_ values for training swatches
        Float[,] xyzOutput = new Float[24, 3];
        Float sensorWhiteG = InnerProduct(sensorIllum!, g_bar);
        Float sensorWhiteY = InnerProduct(sensorIllum!, Spectra.Y());
        for (int i = 0; i < nSwatchReflectances; ++i)
        {
            Spectrum s = swatchReflectances[i];
            XYZ xyz = ProjectReflectanceXYZ(s, outputColorSpace.illuminant, Spectra.X(), Spectra.Y(), Spectra.Z()) *
                      (sensorWhiteY / sensorWhiteG);
            for (int c = 0; c < 3; ++c)
                xyzOutput[i, c] = xyz[c];
        }

        // Initialize _XYZFromSensorRGB_ using linear least squares
        SquareMatrix3? m = LinearLeastSquares(rgbCamera, xyzOutput, nSwatchReflectances);
        if (!m.HasValue)
            ErrorExit("Sensor XYZ from RGB matrix could not be solved.");
        XYZFromSensorRGB = m!.Value;
    }

    public PixelSensor(RGBColorSpace outputColorSpace, Spectrum? sensorIllum, Float imagingRatio)
    {
        r_bar = new DenselySampledSpectrum(Spectra.X());
        g_bar = new DenselySampledSpectrum(Spectra.Y());
        b_bar = new DenselySampledSpectrum(Spectra.Z());
        this.imagingRatio = imagingRatio;
        // Compute white balancing matrix for XYZ _PixelSensor_
        if (sensorIllum != null)
        {
            Point2f sourceWhite = SpectrumToXYZ(sensorIllum).xy();
            Point2f targetWhite = outputColorSpace.w;
            XYZFromSensorRGB = WhiteBalance(sourceWhite, targetWhite);
        }
    }

    public RGB ToSensorRGB(SampledSpectrum L, in SampledWavelengths lambda)
    {
        L = SafeDiv(L, lambda.PDF());
        return imagingRatio * new RGB((r_bar.Sample(lambda) * L).Average(), (g_bar.Sample(lambda) * L).Average(),
                                      (b_bar.Sample(lambda) * L).Average());
    }

    // PixelSensor Private Methods
    // (pbrt's ProjectReflectance<Triplet>() template)
    private static (Float, Float, Float) ProjectReflectance(Spectrum refl, Spectrum illum, Spectrum b1, Spectrum b2,
                                                            Spectrum b3, out Float g_integral)
    {
        Float r0 = 0, r1 = 0, r2 = 0;
        g_integral = 0;
        for (Float lambda = Lambda_min; lambda <= Lambda_max; ++lambda)
        {
            g_integral += b2.Evaluate(lambda) * illum.Evaluate(lambda);
            r0 += b1.Evaluate(lambda) * refl.Evaluate(lambda) * illum.Evaluate(lambda);
            r1 += b2.Evaluate(lambda) * refl.Evaluate(lambda) * illum.Evaluate(lambda);
            r2 += b3.Evaluate(lambda) * refl.Evaluate(lambda) * illum.Evaluate(lambda);
        }
        return (r0, r1, r2);
    }

    private static RGB ProjectReflectanceRGB(Spectrum refl, Spectrum illum, Spectrum b1, Spectrum b2, Spectrum b3)
    {
        var (r0, r1, r2) = ProjectReflectance(refl, illum, b1, b2, b3, out Float g_integral);
        return new RGB(r0, r1, r2) / g_integral;
    }

    private static XYZ ProjectReflectanceXYZ(Spectrum refl, Spectrum illum, Spectrum b1, Spectrum b2, Spectrum b3)
    {
        var (r0, r1, r2) = ProjectReflectance(refl, illum, b1, b2, b3, out Float g_integral);
        return new XYZ(r0, r1, r2) / g_integral;
    }

    // PixelSensor Method Definitions
    public static PixelSensor Create(ParameterDictionary parameters, RGBColorSpace colorSpace, Float exposureTime,
                                     FileLoc? loc)
    {
        // Imaging ratio parameters
        // The defaults here represent a "passthrough" setup such that the imaging
        // ratio will be exactly 1. This is a useful default since scenes that
        // weren't authored with a physical camera in mind will render as expected.
        Float ISO = parameters.GetOneFloat("iso", 100f);
        Float whiteBalanceTemp = parameters.GetOneFloat("whitebalance", 0);

        string sensorName = parameters.GetOneString("sensor", "cie1931");

        // Pass through 0 for cie1931 if it's unspecified so that it doesn't do
        // any white balancing. For actual sensors, 6500 is the default...
        if (sensorName != "cie1931" && whiteBalanceTemp == 0)
            whiteBalanceTemp = 6500;

        // Note: in the talk we mention using 312.5 for historical reasons. The
        // choice of 100 here just means that the other parameters make nice
        // "round" numbers like 1 and 100.
        Float imagingRatio = exposureTime * ISO / 100;

        DenselySampledSpectrum dIllum = Spectra.D(whiteBalanceTemp == 0 ? 6500f : whiteBalanceTemp);
        Spectrum? sensorIllum = whiteBalanceTemp != 0 ? dIllum : null;

        if (sensorName == "cie1931")
            return new PixelSensor(colorSpace, sensorIllum, imagingRatio);
        else
        {
            Spectrum? r = GetNamedSpectrum(sensorName + "_r");
            Spectrum? g = GetNamedSpectrum(sensorName + "_g");
            Spectrum? b = GetNamedSpectrum(sensorName + "_b");

            if (r == null || g == null || b == null)
                ErrorExit(loc, $"{sensorName}: unknown sensor type");

            return new PixelSensor(r!, g!, b!, colorSpace, sensorIllum, imagingRatio);
        }
    }

    public static PixelSensor CreateDefault() => Create(new ParameterDictionary(), RGBColorSpace.sRGB, 1.0f, null);

    // Swatch reflectances are taken from Danny Pascale's Macbeth chart measurements
    // BabelColor ColorChecker data: Copyright (c) 2004-2012 Danny Pascale
    // (www.babelcolor.com); used by permission.
    // http://www.babelcolor.com/index_htm_files/ColorChecker_RGB_and_spectra.zip
    private static readonly Spectrum[] swatchReflectances = Array.ConvertAll(
        new double[][]
        {
        [380.0, 0.055, 390.0, 0.058, 400.0, 0.061, 410.0, 0.062, 420.0, 0.062, 430.0, 0.062, 440.0, 0.062, 450.0, 0.062, 460.0, 0.062, 470.0, 0.062, 480.0, 0.062, 490.0, 0.063, 500.0, 0.065, 510.0, 0.070, 520.0, 0.076, 530.0, 0.079, 540.0, 0.081, 550.0, 0.084, 560.0, 0.091, 570.0, 0.103, 580.0, 0.119, 590.0, 0.134, 600.0, 0.143, 610.0, 0.147, 620.0, 0.151, 630.0, 0.158, 640.0, 0.168, 650.0, 0.179, 660.0, 0.188, 670.0, 0.190, 680.0, 0.186, 690.0, 0.181, 700.0, 0.182, 710.0, 0.187, 720.0, 0.196, 730.0, 0.209],
        [380.0, 0.117, 390.0, 0.143, 400.0, 0.175, 410.0, 0.191, 420.0, 0.196, 430.0, 0.199, 440.0, 0.204, 450.0, 0.213, 460.0, 0.228, 470.0, 0.251, 480.0, 0.280, 490.0, 0.309, 500.0, 0.329, 510.0, 0.333, 520.0, 0.315, 530.0, 0.286, 540.0, 0.273, 550.0, 0.276, 560.0, 0.277, 570.0, 0.289, 580.0, 0.339, 590.0, 0.420, 600.0, 0.488, 610.0, 0.525, 620.0, 0.546, 630.0, 0.562, 640.0, 0.578, 650.0, 0.595, 660.0, 0.612, 670.0, 0.625, 680.0, 0.638, 690.0, 0.656, 700.0, 0.678, 710.0, 0.700, 720.0, 0.717, 730.0, 0.734],
        [380.0, 0.130, 390.0, 0.177, 400.0, 0.251, 410.0, 0.306, 420.0, 0.324, 430.0, 0.330, 440.0, 0.333, 450.0, 0.331, 460.0, 0.323, 470.0, 0.311, 480.0, 0.298, 490.0, 0.285, 500.0, 0.269, 510.0, 0.250, 520.0, 0.231, 530.0, 0.214, 540.0, 0.199, 550.0, 0.185, 560.0, 0.169, 570.0, 0.157, 580.0, 0.149, 590.0, 0.145, 600.0, 0.142, 610.0, 0.141, 620.0, 0.141, 630.0, 0.141, 640.0, 0.143, 650.0, 0.147, 660.0, 0.152, 670.0, 0.154, 680.0, 0.150, 690.0, 0.144, 700.0, 0.136, 710.0, 0.132, 720.0, 0.135, 730.0, 0.147],
        [380.0, 0.051, 390.0, 0.054, 400.0, 0.056, 410.0, 0.057, 420.0, 0.058, 430.0, 0.059, 440.0, 0.060, 450.0, 0.061, 460.0, 0.062, 470.0, 0.063, 480.0, 0.065, 490.0, 0.067, 500.0, 0.075, 510.0, 0.101, 520.0, 0.145, 530.0, 0.178, 540.0, 0.184, 550.0, 0.170, 560.0, 0.149, 570.0, 0.133, 580.0, 0.122, 590.0, 0.115, 600.0, 0.109, 610.0, 0.105, 620.0, 0.104, 630.0, 0.106, 640.0, 0.109, 650.0, 0.112, 660.0, 0.114, 670.0, 0.114, 680.0, 0.112, 690.0, 0.112, 700.0, 0.115, 710.0, 0.120, 720.0, 0.125, 730.0, 0.130],
        [380.0, 0.144, 390.0, 0.198, 400.0, 0.294, 410.0, 0.375, 420.0, 0.408, 430.0, 0.421, 440.0, 0.426, 450.0, 0.426, 460.0, 0.419, 470.0, 0.403, 480.0, 0.379, 490.0, 0.346, 500.0, 0.311, 510.0, 0.281, 520.0, 0.254, 530.0, 0.229, 540.0, 0.214, 550.0, 0.208, 560.0, 0.202, 570.0, 0.194, 580.0, 0.193, 590.0, 0.200, 600.0, 0.214, 610.0, 0.230, 620.0, 0.241, 630.0, 0.254, 640.0, 0.279, 650.0, 0.313, 660.0, 0.348, 670.0, 0.366, 680.0, 0.366, 690.0, 0.359, 700.0, 0.358, 710.0, 0.365, 720.0, 0.377, 730.0, 0.398],
        [380.0, 0.136, 390.0, 0.179, 400.0, 0.247, 410.0, 0.297, 420.0, 0.320, 430.0, 0.337, 440.0, 0.355, 450.0, 0.381, 460.0, 0.419, 470.0, 0.466, 480.0, 0.510, 490.0, 0.546, 500.0, 0.567, 510.0, 0.574, 520.0, 0.569, 530.0, 0.551, 540.0, 0.524, 550.0, 0.488, 560.0, 0.445, 570.0, 0.400, 580.0, 0.350, 590.0, 0.299, 600.0, 0.252, 610.0, 0.221, 620.0, 0.204, 630.0, 0.196, 640.0, 0.191, 650.0, 0.188, 660.0, 0.191, 670.0, 0.199, 680.0, 0.212, 690.0, 0.223, 700.0, 0.232, 710.0, 0.233, 720.0, 0.229, 730.0, 0.229],
        [380.0, 0.054, 390.0, 0.054, 400.0, 0.053, 410.0, 0.054, 420.0, 0.054, 430.0, 0.055, 440.0, 0.055, 450.0, 0.055, 460.0, 0.056, 470.0, 0.057, 480.0, 0.058, 490.0, 0.061, 500.0, 0.068, 510.0, 0.089, 520.0, 0.125, 530.0, 0.154, 540.0, 0.174, 550.0, 0.199, 560.0, 0.248, 570.0, 0.335, 580.0, 0.444, 590.0, 0.538, 600.0, 0.587, 610.0, 0.595, 620.0, 0.591, 630.0, 0.587, 640.0, 0.584, 650.0, 0.584, 660.0, 0.590, 670.0, 0.603, 680.0, 0.620, 690.0, 0.639, 700.0, 0.655, 710.0, 0.663, 720.0, 0.663, 730.0, 0.667],
        [380.0, 0.122, 390.0, 0.164, 400.0, 0.229, 410.0, 0.286, 420.0, 0.327, 430.0, 0.361, 440.0, 0.388, 450.0, 0.400, 460.0, 0.392, 470.0, 0.362, 480.0, 0.316, 490.0, 0.260, 500.0, 0.209, 510.0, 0.168, 520.0, 0.138, 530.0, 0.117, 540.0, 0.104, 550.0, 0.096, 560.0, 0.090, 570.0, 0.086, 580.0, 0.084, 590.0, 0.084, 600.0, 0.084, 610.0, 0.084, 620.0, 0.084, 630.0, 0.085, 640.0, 0.090, 650.0, 0.098, 660.0, 0.109, 670.0, 0.123, 680.0, 0.143, 690.0, 0.169, 700.0, 0.205, 710.0, 0.244, 720.0, 0.287, 730.0, 0.332],
        [380.0, 0.096, 390.0, 0.115, 400.0, 0.131, 410.0, 0.135, 420.0, 0.133, 430.0, 0.132, 440.0, 0.130, 450.0, 0.128, 460.0, 0.125, 470.0, 0.120, 480.0, 0.115, 490.0, 0.110, 500.0, 0.105, 510.0, 0.100, 520.0, 0.095, 530.0, 0.093, 540.0, 0.092, 550.0, 0.093, 560.0, 0.096, 570.0, 0.108, 580.0, 0.156, 590.0, 0.265, 600.0, 0.399, 610.0, 0.500, 620.0, 0.556, 630.0, 0.579, 640.0, 0.588, 650.0, 0.591, 660.0, 0.593, 670.0, 0.594, 680.0, 0.598, 690.0, 0.602, 700.0, 0.607, 710.0, 0.609, 720.0, 0.609, 730.0, 0.610],
        [380.0, 0.092, 390.0, 0.116, 400.0, 0.146, 410.0, 0.169, 420.0, 0.178, 430.0, 0.173, 440.0, 0.158, 450.0, 0.139, 460.0, 0.119, 470.0, 0.101, 480.0, 0.087, 490.0, 0.075, 500.0, 0.066, 510.0, 0.060, 520.0, 0.056, 530.0, 0.053, 540.0, 0.051, 550.0, 0.051, 560.0, 0.052, 570.0, 0.052, 580.0, 0.051, 590.0, 0.052, 600.0, 0.058, 610.0, 0.073, 620.0, 0.096, 630.0, 0.119, 640.0, 0.141, 650.0, 0.166, 660.0, 0.194, 670.0, 0.227, 680.0, 0.265, 690.0, 0.309, 700.0, 0.355, 710.0, 0.396, 720.0, 0.436, 730.0, 0.478],
        [380.0, 0.061, 390.0, 0.061, 400.0, 0.062, 410.0, 0.063, 420.0, 0.064, 430.0, 0.066, 440.0, 0.069, 450.0, 0.075, 460.0, 0.085, 470.0, 0.105, 480.0, 0.139, 490.0, 0.192, 500.0, 0.271, 510.0, 0.376, 520.0, 0.476, 530.0, 0.531, 540.0, 0.549, 550.0, 0.546, 560.0, 0.528, 570.0, 0.504, 580.0, 0.471, 590.0, 0.428, 600.0, 0.381, 610.0, 0.347, 620.0, 0.327, 630.0, 0.318, 640.0, 0.312, 650.0, 0.310, 660.0, 0.314, 670.0, 0.327, 680.0, 0.345, 690.0, 0.363, 700.0, 0.376, 710.0, 0.381, 720.0, 0.378, 730.0, 0.379],
        [380.0, 0.063, 390.0, 0.063, 400.0, 0.063, 410.0, 0.064, 420.0, 0.064, 430.0, 0.064, 440.0, 0.065, 450.0, 0.066, 460.0, 0.067, 470.0, 0.068, 480.0, 0.071, 490.0, 0.076, 500.0, 0.087, 510.0, 0.125, 520.0, 0.206, 530.0, 0.305, 540.0, 0.383, 550.0, 0.431, 560.0, 0.469, 570.0, 0.518, 580.0, 0.568, 590.0, 0.607, 600.0, 0.628, 610.0, 0.637, 620.0, 0.640, 630.0, 0.642, 640.0, 0.645, 650.0, 0.648, 660.0, 0.651, 670.0, 0.653, 680.0, 0.657, 690.0, 0.664, 700.0, 0.673, 710.0, 0.680, 720.0, 0.684, 730.0, 0.688],
        [380.0, 0.066, 390.0, 0.079, 400.0, 0.102, 410.0, 0.146, 420.0, 0.200, 430.0, 0.244, 440.0, 0.282, 450.0, 0.309, 460.0, 0.308, 470.0, 0.278, 480.0, 0.231, 490.0, 0.178, 500.0, 0.130, 510.0, 0.094, 520.0, 0.070, 530.0, 0.054, 540.0, 0.046, 550.0, 0.042, 560.0, 0.039, 570.0, 0.038, 580.0, 0.038, 590.0, 0.038, 600.0, 0.038, 610.0, 0.039, 620.0, 0.039, 630.0, 0.040, 640.0, 0.041, 650.0, 0.042, 660.0, 0.044, 670.0, 0.045, 680.0, 0.046, 690.0, 0.046, 700.0, 0.048, 710.0, 0.052, 720.0, 0.057, 730.0, 0.065],
        [380.0, 0.052, 390.0, 0.053, 400.0, 0.054, 410.0, 0.055, 420.0, 0.057, 430.0, 0.059, 440.0, 0.061, 450.0, 0.066, 460.0, 0.075, 470.0, 0.093, 480.0, 0.125, 490.0, 0.178, 500.0, 0.246, 510.0, 0.307, 520.0, 0.337, 530.0, 0.334, 540.0, 0.317, 550.0, 0.293, 560.0, 0.262, 570.0, 0.230, 580.0, 0.198, 590.0, 0.165, 600.0, 0.135, 610.0, 0.115, 620.0, 0.104, 630.0, 0.098, 640.0, 0.094, 650.0, 0.092, 660.0, 0.093, 670.0, 0.097, 680.0, 0.102, 690.0, 0.108, 700.0, 0.113, 710.0, 0.115, 720.0, 0.114, 730.0, 0.114],
        [380.0, 0.050, 390.0, 0.049, 400.0, 0.048, 410.0, 0.047, 420.0, 0.047, 430.0, 0.047, 440.0, 0.047, 450.0, 0.047, 460.0, 0.046, 470.0, 0.045, 480.0, 0.044, 490.0, 0.044, 500.0, 0.045, 510.0, 0.046, 520.0, 0.047, 530.0, 0.048, 540.0, 0.049, 550.0, 0.050, 560.0, 0.054, 570.0, 0.060, 580.0, 0.072, 590.0, 0.104, 600.0, 0.178, 610.0, 0.312, 620.0, 0.467, 630.0, 0.581, 640.0, 0.644, 650.0, 0.675, 660.0, 0.690, 670.0, 0.698, 680.0, 0.706, 690.0, 0.715, 700.0, 0.724, 710.0, 0.730, 720.0, 0.734, 730.0, 0.738],
        [380.0, 0.058, 390.0, 0.054, 400.0, 0.052, 410.0, 0.052, 420.0, 0.053, 430.0, 0.054, 440.0, 0.056, 450.0, 0.059, 460.0, 0.067, 470.0, 0.081, 480.0, 0.107, 490.0, 0.152, 500.0, 0.225, 510.0, 0.336, 520.0, 0.462, 530.0, 0.559, 540.0, 0.616, 550.0, 0.650, 560.0, 0.672, 570.0, 0.694, 580.0, 0.710, 590.0, 0.723, 600.0, 0.731, 610.0, 0.739, 620.0, 0.746, 630.0, 0.752, 640.0, 0.758, 650.0, 0.764, 660.0, 0.769, 670.0, 0.771, 680.0, 0.776, 690.0, 0.782, 700.0, 0.790, 710.0, 0.796, 720.0, 0.799, 730.0, 0.804],
        [380.0, 0.145, 390.0, 0.195, 400.0, 0.283, 410.0, 0.346, 420.0, 0.362, 430.0, 0.354, 440.0, 0.334, 450.0, 0.306, 460.0, 0.276, 470.0, 0.248, 480.0, 0.218, 490.0, 0.190, 500.0, 0.168, 510.0, 0.149, 520.0, 0.127, 530.0, 0.107, 540.0, 0.100, 550.0, 0.102, 560.0, 0.104, 570.0, 0.109, 580.0, 0.137, 590.0, 0.200, 600.0, 0.290, 610.0, 0.400, 620.0, 0.516, 630.0, 0.615, 640.0, 0.687, 650.0, 0.732, 660.0, 0.760, 670.0, 0.774, 680.0, 0.783, 690.0, 0.793, 700.0, 0.803, 710.0, 0.812, 720.0, 0.817, 730.0, 0.825],
        [380.0, 0.108, 390.0, 0.141, 400.0, 0.192, 410.0, 0.236, 420.0, 0.261, 430.0, 0.286, 440.0, 0.317, 450.0, 0.353, 460.0, 0.390, 470.0, 0.426, 480.0, 0.446, 490.0, 0.444, 500.0, 0.423, 510.0, 0.385, 520.0, 0.337, 530.0, 0.283, 540.0, 0.231, 550.0, 0.185, 560.0, 0.146, 570.0, 0.118, 580.0, 0.101, 590.0, 0.090, 600.0, 0.082, 610.0, 0.076, 620.0, 0.074, 630.0, 0.073, 640.0, 0.073, 650.0, 0.074, 660.0, 0.076, 670.0, 0.077, 680.0, 0.076, 690.0, 0.075, 700.0, 0.073, 710.0, 0.072, 720.0, 0.074, 730.0, 0.079],
        [380.0, 0.189, 390.0, 0.255, 400.0, 0.423, 410.0, 0.660, 420.0, 0.811, 430.0, 0.862, 440.0, 0.877, 450.0, 0.884, 460.0, 0.891, 470.0, 0.896, 480.0, 0.899, 490.0, 0.904, 500.0, 0.907, 510.0, 0.909, 520.0, 0.911, 530.0, 0.910, 540.0, 0.911, 550.0, 0.914, 560.0, 0.913, 570.0, 0.916, 580.0, 0.915, 590.0, 0.916, 600.0, 0.914, 610.0, 0.915, 620.0, 0.918, 630.0, 0.919, 640.0, 0.921, 650.0, 0.923, 660.0, 0.924, 670.0, 0.922, 680.0, 0.922, 690.0, 0.925, 700.0, 0.927, 710.0, 0.930, 720.0, 0.930, 730.0, 0.933],
        [380.0, 0.171, 390.0, 0.232, 400.0, 0.365, 410.0, 0.507, 420.0, 0.567, 430.0, 0.583, 440.0, 0.588, 450.0, 0.590, 460.0, 0.591, 470.0, 0.590, 480.0, 0.588, 490.0, 0.588, 500.0, 0.589, 510.0, 0.589, 520.0, 0.591, 530.0, 0.590, 540.0, 0.590, 550.0, 0.590, 560.0, 0.589, 570.0, 0.591, 580.0, 0.590, 590.0, 0.590, 600.0, 0.587, 610.0, 0.585, 620.0, 0.583, 630.0, 0.580, 640.0, 0.578, 650.0, 0.576, 660.0, 0.574, 670.0, 0.572, 680.0, 0.571, 690.0, 0.569, 700.0, 0.568, 710.0, 0.568, 720.0, 0.566, 730.0, 0.566],
        [380.0, 0.144, 390.0, 0.192, 400.0, 0.272, 410.0, 0.331, 420.0, 0.350, 430.0, 0.357, 440.0, 0.361, 450.0, 0.363, 460.0, 0.363, 470.0, 0.361, 480.0, 0.359, 490.0, 0.358, 500.0, 0.358, 510.0, 0.359, 520.0, 0.360, 530.0, 0.360, 540.0, 0.361, 550.0, 0.361, 560.0, 0.360, 570.0, 0.362, 580.0, 0.362, 590.0, 0.361, 600.0, 0.359, 610.0, 0.358, 620.0, 0.355, 630.0, 0.352, 640.0, 0.350, 650.0, 0.348, 660.0, 0.345, 670.0, 0.343, 680.0, 0.340, 690.0, 0.338, 700.0, 0.335, 710.0, 0.334, 720.0, 0.332, 730.0, 0.331],
        [380.0, 0.105, 390.0, 0.131, 400.0, 0.163, 410.0, 0.180, 420.0, 0.186, 430.0, 0.190, 440.0, 0.193, 450.0, 0.194, 460.0, 0.194, 470.0, 0.192, 480.0, 0.191, 490.0, 0.191, 500.0, 0.191, 510.0, 0.192, 520.0, 0.192, 530.0, 0.192, 540.0, 0.192, 550.0, 0.192, 560.0, 0.192, 570.0, 0.193, 580.0, 0.192, 590.0, 0.192, 600.0, 0.191, 610.0, 0.189, 620.0, 0.188, 630.0, 0.186, 640.0, 0.184, 650.0, 0.182, 660.0, 0.181, 670.0, 0.179, 680.0, 0.178, 690.0, 0.176, 700.0, 0.174, 710.0, 0.173, 720.0, 0.172, 730.0, 0.171],
        [380.0, 0.068, 390.0, 0.077, 400.0, 0.084, 410.0, 0.087, 420.0, 0.089, 430.0, 0.090, 440.0, 0.092, 450.0, 0.092, 460.0, 0.091, 470.0, 0.090, 480.0, 0.090, 490.0, 0.090, 500.0, 0.090, 510.0, 0.090, 520.0, 0.090, 530.0, 0.090, 540.0, 0.090, 550.0, 0.090, 560.0, 0.090, 570.0, 0.090, 580.0, 0.090, 590.0, 0.089, 600.0, 0.089, 610.0, 0.088, 620.0, 0.087, 630.0, 0.086, 640.0, 0.086, 650.0, 0.085, 660.0, 0.084, 670.0, 0.084, 680.0, 0.083, 690.0, 0.083, 700.0, 0.082, 710.0, 0.081, 720.0, 0.081, 730.0, 0.081],
        [380.0, 0.031, 390.0, 0.032, 400.0, 0.032, 410.0, 0.033, 420.0, 0.033, 430.0, 0.033, 440.0, 0.033, 450.0, 0.033, 460.0, 0.032, 470.0, 0.032, 480.0, 0.032, 490.0, 0.032, 500.0, 0.032, 510.0, 0.032, 520.0, 0.032, 530.0, 0.032, 540.0, 0.032, 550.0, 0.032, 560.0, 0.032, 570.0, 0.032, 580.0, 0.032, 590.0, 0.032, 600.0, 0.032, 610.0, 0.032, 620.0, 0.032, 630.0, 0.032, 640.0, 0.032, 650.0, 0.032, 660.0, 0.032, 670.0, 0.032, 680.0, 0.032, 690.0, 0.032, 700.0, 0.032, 710.0, 0.032, 720.0, 0.032, 730.0, 0.033]
        },
        s => (Spectrum)PiecewiseLinearSpectrum.FromInterleaved(Array.ConvertAll(s, v => (Float)v), false));
}

// VisibleSurface Definition
public struct VisibleSurface
{
    // VisibleSurface Public Members
    public Point3f p;
    public Normal3f n, ns;
    public Point2f uv;
    public Float time = 0;
    public Vector3f dpdx, dpdy;
    public SampledSpectrum albedo;
    public bool set = false;

    // VisibleSurface Public Methods
    public VisibleSurface() { }

    // VisibleSurface Method Definitions
    public VisibleSurface(in SurfaceInteraction si, in SampledSpectrum albedo, in SampledWavelengths lambda)
    {
        this.albedo = albedo;
        set = true;
        // Initialize geometric _VisibleSurface_ members
        p = si.p();
        Vector3f wo = si.wo;
        n = FaceForward(si.n, wo);
        ns = FaceForward(si.shading.n, wo);
        uv = si.uv;
        time = si.time;
        dpdx = si.dpdx;
        dpdy = si.dpdy;
    }

    public static bool operator true(in VisibleSurface v) => v.set;
    public static bool operator false(in VisibleSurface v) => !v.set;

    public override readonly string ToString() =>
        $"[ VisibleSurface set: {(set ? "true" : "false")} p: {p} n: {n} ns: {ns} dpdx: {dpdx} dpdy: {dpdy} " +
        $"time: {FloatToString(time)} albedo: {albedo} ]";
}

// FilmBaseParameters Definition
public sealed class FilmBaseParameters
{
    public Point2i fullResolution;
    public Bounds2i pixelBounds;
    public Filter filter;
    public Float diagonal;
    public PixelSensor sensor;
    public string filename;

    public FilmBaseParameters(Point2i fullResolution, Bounds2i pixelBounds, Filter filter, Float diagonal,
                              PixelSensor sensor, string filename)
    {
        this.fullResolution = fullResolution;
        this.pixelBounds = pixelBounds;
        this.filter = filter;
        this.diagonal = diagonal;
        this.sensor = sensor;
        this.filename = filename;
    }

    // FilmBaseParameters Method Definitions
    public FilmBaseParameters(ParameterDictionary parameters, Filter filter, PixelSensor sensor, FileLoc? loc)
    {
        this.filter = filter;
        this.sensor = sensor;
        filename = parameters.GetOneString("filename", "");
        if (Options.imageFile.Length != 0)
        {
            if (filename.Length != 0)
                Warning(loc, $"Output filename supplied on command line, \"{Options.imageFile}\" will override " +
                             $"filename provided in scene description file, \"{filename}\".");
            filename = Options.imageFile;
        }
        else if (filename.Length == 0)
            filename = "pbrt.exr";

        if (Options.fullscreen)
        {
            fullResolution = GUI.GetResolution();

            // Omit unused parameter error
            _ = parameters.GetOneInt("xresolution", 1280);
            _ = parameters.GetOneInt("yresolution", 720);
        }
        else
            fullResolution = new Point2i(parameters.GetOneInt("xresolution", 1280),
                                         parameters.GetOneInt("yresolution", 720));
        if (Options.quickRender)
        {
            fullResolution.x = Math.Max(1, fullResolution.x / 4);
            fullResolution.y = Math.Max(1, fullResolution.y / 4);
        }

        pixelBounds = new Bounds2i(new Point2i(0, 0), fullResolution);
        List<int> pb = parameters.GetIntArray("pixelbounds");
        if (Options.pixelBounds is { } optPixelBounds)
        {
            Bounds2i newBounds = optPixelBounds;
            if (Intersect(newBounds, pixelBounds) != newBounds)
                Warning(loc, "Supplied pixel bounds extend beyond image resolution. Clamping.");
            pixelBounds = Intersect(newBounds, pixelBounds);

            if (pb.Count != 0)
                Warning(loc, "Both pixel bounds and crop window were specified. Using the crop window.");
        }
        else if (pb.Count != 0)
        {
            if (pb.Count != 4)
                Error(loc, $"{pb.Count} values supplied for \"pixelbounds\". Expected 4.");
            else
            {
                Bounds2i newBounds = new(new Point2i(pb[0], pb[2]), new Point2i(pb[1], pb[3]));
                if (Intersect(newBounds, pixelBounds) != newBounds)
                    Warning(loc, "Supplied pixel bounds extend beyond image resolution. Clamping.");
                pixelBounds = Intersect(newBounds, pixelBounds);
            }
        }

        List<Float> cr = parameters.GetFloatArray("cropwindow");
        if (Options.cropWindow is { } optCrop)
        {
            Bounds2f crop = optCrop;
            if (Intersect(crop, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1))) != crop)
            {
                Error(loc, $"Film crop window {crop} is not in [0,1] range; did you mean to use \"pixelbounds\"? " +
                           "Clamping to valid range.");
                crop = Intersect(crop, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));
            }

            // Compute film image bounds
            pixelBounds = CropPixelBounds(fullResolution, crop);

            if (cr.Count != 0)
                Warning(loc, "Crop window supplied on command line will override crop window specified with Film.");
            if (Options.pixelBounds.HasValue || pb.Count != 0)
                Warning(loc, "Both pixel bounds and crop window were specified. Using the crop window.");
        }
        else if (cr.Count != 0)
        {
            if (Options.pixelBounds.HasValue)
                Warning(loc, "Ignoring \"cropwindow\" since pixel bounds were specified on the command line.");
            else if (cr.Count == 4)
            {
                if (pb.Count != 0)
                    Warning(loc, "Both pixel bounds and crop window were specified. Using the crop window.");

                Bounds2f crop = new();
                crop.pMin.x = Clamp(FMath.Min(cr[0], cr[1]), 0f, 1f);
                crop.pMax.x = Clamp(FMath.Max(cr[0], cr[1]), 0f, 1f);
                crop.pMin.y = Clamp(FMath.Min(cr[2], cr[3]), 0f, 1f);
                crop.pMax.y = Clamp(FMath.Max(cr[2], cr[3]), 0f, 1f);

                // Compute film image bounds
                pixelBounds = CropPixelBounds(fullResolution, crop);
            }
            else
                Error(loc, $"{cr.Count} values supplied for \"cropwindow\". Expected 4.");
        }

        if (pixelBounds.IsEmpty())
            ErrorExit(loc, $"Degenerate pixel bounds provided to film: {pixelBounds}.");

        diagonal = parameters.GetOneFloat("diagonal", 35f);
    }

    private static Bounds2i CropPixelBounds(Point2i fullResolution, in Bounds2f crop) =>
        new(new Point2i((int)FMath.Ceiling(fullResolution.x * crop.pMin.x),
                        (int)FMath.Ceiling(fullResolution.y * crop.pMin.y)),
            new Point2i((int)FMath.Ceiling(fullResolution.x * crop.pMax.x),
                        (int)FMath.Ceiling(fullResolution.y * crop.pMax.y)));
}

// FilmBase Definition
public abstract class FilmBase : Film
{
    protected static readonly StatMemoryCounter filmPixelMemory = new("Memory/Film pixels");

    // FilmBase Protected Members
    protected Point2i fullResolution;
    protected Bounds2i pixelBounds;
    protected Filter filter;
    protected Float diagonal;
    protected PixelSensor sensor;
    protected string filename;

    // FilmBase Public Methods
    protected FilmBase(FilmBaseParameters p)
    {
        fullResolution = p.fullResolution;
        pixelBounds = p.pixelBounds;
        filter = p.filter;
        diagonal = p.diagonal * .001f;
        sensor = p.sensor;
        filename = p.filename;
        CHECK(!pixelBounds.IsEmpty());
        CHECK_GE(pixelBounds.pMin.x, 0);
        CHECK_LE(pixelBounds.pMax.x, fullResolution.x);
        CHECK_GE(pixelBounds.pMin.y, 0);
        CHECK_LE(pixelBounds.pMax.y, fullResolution.y);
        LOG_VERBOSE($"Created film with full resolution {fullResolution}, pixelBounds {pixelBounds}");
    }

    public override Point2i FullResolution() => fullResolution;
    public override Bounds2i PixelBounds() => pixelBounds;
    public override Float Diagonal() => diagonal;
    public override Filter GetFilter() => filter;
    public override PixelSensor GetPixelSensor() => sensor;
    public override string GetFilename() => filename;

    public override SampledWavelengths SampleWavelengths(Float u) => SampledWavelengths.SampleVisible(u);

    // FilmBase Method Definitions
    public override Bounds2f SampleBounds()
    {
        Vector2f radius = filter.Radius();
        return new Bounds2f(new Point2f(pixelBounds.pMin) - radius + new Vector2f(0.5f, 0.5f),
                            new Point2f(pixelBounds.pMax) + radius - new Vector2f(0.5f, 0.5f));
    }

    public string BaseToString() =>
        $"fullResolution: {fullResolution} diagonal: {FloatToString(diagonal)} filter: {filter} " +
        $"filename: {filename} pixelBounds: {pixelBounds}";

    // The bounds of the pixels that a splat at _p_ touches (shared by the
    // films' AddSplat()).
    protected Bounds2i SplatBounds(Point2f p)
    {
        // Compute bounds of affected pixels for splat, _splatBounds_
        Point2f pDiscrete = p + new Vector2f(0.5f, 0.5f);
        Vector2f radius = filter.Radius();
        Bounds2i splatBounds = new(new Point2i(Floor(pDiscrete - radius)),
                                   new Point2i(Floor(pDiscrete + radius)) + new Vector2i(1, 1));
        return Intersect(splatBounds, pixelBounds);
    }

    protected static string B(bool b) => b ? "true" : "false";
}

// RGBFilm Definition
public sealed class RGBFilm : FilmBase
{
    // RGBFilm::Pixel Definition
    private struct Pixel
    {
        public Double3 rgbSum;
        public double weightSum;
        public AtomicDouble3 rgbSplat;
    }

    // RGBFilm Private Members
    private readonly RGBColorSpace colorSpace;
    private readonly Float maxComponentValue;
    private readonly bool writeFP16;
    private readonly Float filterIntegral;
    private readonly SquareMatrix3 outputRGBFromSensorRGB;
    private readonly Array2D<Pixel> pixels;

    // RGBFilm Public Methods
    public override bool UsesVisibleSurface() => false;

    public override void AddSample(Point2i pFilm, SampledSpectrum L, in SampledWavelengths lambda,
                                   in VisibleSurface visibleSurface, Float weight)
    {
        // Convert sample radiance to _PixelSensor_ RGB
        RGB rgb = sensor.ToSensorRGB(L, lambda);

        // Optionally clamp sensor RGB value
        Float m = FMath.Max(rgb.r, FMath.Max(rgb.g, rgb.b));
        if (m > maxComponentValue)
            rgb *= maxComponentValue / m;

        DCHECK(InsideExclusive(pFilm, pixelBounds));
        // Update pixel values with filtered sample contribution
        ref Pixel pixel = ref pixels[pFilm];
        for (int c = 0; c < 3; ++c)
            pixel.rgbSum[c] += weight * rgb[c];
        pixel.weightSum += weight;
    }

    public override RGB GetPixelRGB(Point2i p, Float splatScale = 1)
    {
        ref readonly Pixel pixel = ref pixels[p];
        RGB rgb = new((Float)pixel.rgbSum[0], (Float)pixel.rgbSum[1], (Float)pixel.rgbSum[2]);
        // Normalize _rgb_ with weight sum
        Float weightSum = (Float)pixel.weightSum;
        if (weightSum != 0)
            rgb /= weightSum;

        // Add splat value at pixel
        for (int c = 0; c < 3; ++c)
            rgb[c] = (Float)(rgb[c] + splatScale * (double)pixel.rgbSplat[c] / filterIntegral);

        // Convert _rgb_ to output RGB color space
        rgb = outputRGBFromSensorRGB * rgb;

        return rgb;
    }

    // RGBFilm Method Definitions
    public RGBFilm(FilmBaseParameters p, RGBColorSpace colorSpace, Float maxComponentValue = Infinity,
                   bool writeFP16 = true)
        : base(p)
    {
        pixels = new Array2D<Pixel>(p.pixelBounds);
        this.colorSpace = colorSpace;
        this.maxComponentValue = maxComponentValue;
        this.writeFP16 = writeFP16;
        filterIntegral = filter.Integral();
        CHECK(!pixelBounds.IsEmpty());
        CHECK(colorSpace != null);
        // (sizeof(Pixel) in pbrt)
        filmPixelMemory.Add(pixelBounds.Area() * 56L);
        // Compute _outputRGBFromSensorRGB_ matrix
        outputRGBFromSensorRGB = colorSpace!.RGBFromXYZ * sensor.XYZFromSensorRGB;
    }

    public override void AddSplat(Point2f p, SampledSpectrum L, in SampledWavelengths lambda)
    {
        CHECK(!L.HasNaNs());
        // Convert sample radiance to _PixelSensor_ RGB
        RGB rgb = sensor.ToSensorRGB(L, lambda);

        // Optionally clamp sensor RGB value
        Float m = FMath.Max(rgb.r, FMath.Max(rgb.g, rgb.b));
        if (m > maxComponentValue)
            rgb *= maxComponentValue / m;

        // Compute bounds of affected pixels for splat, _splatBounds_
        Bounds2i splatBounds = SplatBounds(p);

        foreach (Point2i pi in splatBounds)
        {
            // Evaluate filter at _pi_ and add splat contribution
            Float wt = filter.Evaluate(new Point2f(p - new Point2f(pi) - new Vector2f(0.5f, 0.5f)));
            if (wt != 0)
            {
                ref Pixel pixel = ref pixels[pi];
                for (int i = 0; i < 3; ++i)
                    pixel.rgbSplat[i].Add(wt * rgb[i]);
            }
        }
    }

    public override void WriteImage(ImageMetadata metadata, Float splatScale = 1)
    {
        metadata = metadata.Clone();
        Image image = GetImage(metadata, splatScale);
        LOG_VERBOSE($"Writing image {filename} with bounds {pixelBounds}");
        image.Write(filename, metadata);
    }

    public override Image GetImage(ImageMetadata metadata, Float splatScale = 1)
    {
        // Convert image to RGB and compute final pixel values
        LOG_VERBOSE($"Converting image to RGB and computing final weighted pixel values");
        PixelFormat format = writeFP16 ? PixelFormat.Half : PixelFormat.Float;
        Image image = new(format, new Point2i(pixelBounds.Diagonal()), ["R", "G", "B"]);

        int nClamped = 0;
        ParallelFor2D(pixelBounds, (Point2i p) =>
        {
            RGB rgb = GetPixelRGB(p, splatScale);

            if (writeFP16 && FMath.Max(rgb.r, FMath.Max(rgb.g, rgb.b)) > 65504)
            {
                if (rgb.r > 65504)
                    rgb.r = 65504;
                if (rgb.g > 65504)
                    rgb.g = 65504;
                if (rgb.b > 65504)
                    rgb.b = 65504;
                Interlocked.Increment(ref nClamped);
            }

            Point2i pOffset = new(p.x - pixelBounds.pMin.x, p.y - pixelBounds.pMin.y);
            image.SetChannels(pOffset, [rgb[0], rgb[1], rgb[2]]);
        });

        if (nClamped > 0)
            Warning($"{nClamped} pixel values clamped to maximum fp16 value.");

        metadata.pixelBounds = pixelBounds;
        metadata.fullResolution = fullResolution;
        metadata.colorSpace = colorSpace;

        return image;
    }

    public override string ToString() =>
        $"[ RGBFilm {BaseToString()} colorSpace: {colorSpace} maxComponentValue: {FloatToString(maxComponentValue)} " +
        $"writeFP16: {B(writeFP16)} ]";

    public static RGBFilm Create(ParameterDictionary parameters, Float exposureTime, Filter filter,
                                 RGBColorSpace colorSpace, FileLoc? loc)
    {
        Float maxComponentValue = parameters.GetOneFloat("maxcomponentvalue", Infinity);
        bool writeFP16 = parameters.GetOneBool("savefp16", true);

        PixelSensor sensor = PixelSensor.Create(parameters, colorSpace, exposureTime, loc);
        FilmBaseParameters filmBaseParameters = new(parameters, filter, sensor, loc);

        return new RGBFilm(filmBaseParameters, colorSpace, maxComponentValue, writeFP16);
    }

    public override RGB ToOutputRGB(SampledSpectrum L, in SampledWavelengths lambda)
    {
        RGB sensorRGB = sensor.ToSensorRGB(L, lambda);
        return outputRGBFromSensorRGB * sensorRGB;
    }

    public override void ResetPixel(Point2i p) => pixels[p] = default;
}

// GBufferFilm Definition
public sealed class GBufferFilm : FilmBase
{
    // GBufferFilm::Pixel Definition
    private struct Pixel
    {
        public Double3 rgbSum;
        public double weightSum, gBufferWeightSum;
        public AtomicDouble3 rgbSplat;
        public Point3f pSum;
        public Float dzdxSum, dzdySum;
        public Normal3f nSum, nsSum;
        public Point2f uvSum;
        public Double3 rgbAlbedoSum;
        public VarianceEstimator3 rgbVariance;
    }

    // GBufferFilm Private Members
    private readonly AnimatedTransform outputFromRender;
    private readonly bool applyInverse;
    private readonly Array2D<Pixel> pixels;
    private readonly RGBColorSpace colorSpace;
    private readonly Float maxComponentValue;
    private readonly bool writeFP16;
    private readonly Float filterIntegral;
    private readonly SquareMatrix3 outputRGBFromSensorRGB;

    // GBufferFilm Method Definitions
    public override void AddSample(Point2i pFilm, SampledSpectrum L, in SampledWavelengths lambda,
                                   in VisibleSurface visibleSurface, Float weight)
    {
        RGB rgb = sensor.ToSensorRGB(L, lambda);
        Float m = FMath.Max(rgb.r, FMath.Max(rgb.g, rgb.b));
        if (m > maxComponentValue)
            rgb *= maxComponentValue / m;

        ref Pixel p = ref pixels[pFilm];
        if (visibleSurface.set)
        {
            p.gBufferWeightSum += weight;

            // Update variance estimates.
            for (int c = 0; c < 3; ++c)
                p.rgbVariance[c].Add(rgb[c]);

            if (applyInverse)
            {
                p.pSum += weight * outputFromRender.ApplyInverse(visibleSurface.p, visibleSurface.time);
                p.nSum += weight * outputFromRender.ApplyInverse(visibleSurface.n, visibleSurface.time);
                p.nsSum += weight * outputFromRender.ApplyInverse(visibleSurface.ns, visibleSurface.time);
                p.dzdxSum += weight * outputFromRender.ApplyInverse(visibleSurface.dpdx, visibleSurface.time).z;
                p.dzdySum += weight * outputFromRender.ApplyInverse(visibleSurface.dpdy, visibleSurface.time).z;
            }
            else
            {
                p.pSum += weight * outputFromRender.Apply(visibleSurface.p, visibleSurface.time);
                p.nSum += weight * outputFromRender.Apply(visibleSurface.n, visibleSurface.time);
                p.nsSum += weight * outputFromRender.Apply(visibleSurface.ns, visibleSurface.time);
                p.dzdxSum += weight * outputFromRender.Apply(visibleSurface.dpdx, visibleSurface.time).z;
                p.dzdySum += weight * outputFromRender.Apply(visibleSurface.dpdy, visibleSurface.time).z;
            }
            p.uvSum += weight * visibleSurface.uv;

            SampledSpectrum albedo = visibleSurface.albedo * colorSpace.illuminant.Sample(lambda);
            RGB albedoRGB = albedo.ToRGB(lambda, colorSpace);
            for (int c = 0; c < 3; ++c)
                p.rgbAlbedoSum[c] += weight * albedoRGB[c];
        }

        for (int c = 0; c < 3; ++c)
            p.rgbSum[c] += rgb[c] * weight;
        p.weightSum += weight;
    }

    public GBufferFilm(FilmBaseParameters p, AnimatedTransform outputFromRender, bool applyInverse,
                       RGBColorSpace colorSpace, Float maxComponentValue = Infinity, bool writeFP16 = true)
        : base(p)
    {
        this.outputFromRender = outputFromRender;
        this.applyInverse = applyInverse;
        pixels = new Array2D<Pixel>(pixelBounds);
        this.colorSpace = colorSpace;
        this.maxComponentValue = maxComponentValue;
        this.writeFP16 = writeFP16;
        filterIntegral = filter.Integral();
        CHECK(!pixelBounds.IsEmpty());
        // (sizeof(Pixel) in pbrt)
        filmPixelMemory.Add(pixelBounds.Area() * 192L);
        outputRGBFromSensorRGB = colorSpace.RGBFromXYZ * sensor.XYZFromSensorRGB;
    }

    public override void AddSplat(Point2f p, SampledSpectrum v, in SampledWavelengths lambda)
    {
        // NOTE: same code as RGBFilm::AddSplat()...
        CHECK(!v.HasNaNs());
        RGB rgb = sensor.ToSensorRGB(v, lambda);
        Float m = FMath.Max(rgb.r, FMath.Max(rgb.g, rgb.b));
        if (m > maxComponentValue)
            rgb *= maxComponentValue / m;

        Bounds2i splatBounds = SplatBounds(p);
        foreach (Point2i pi in splatBounds)
        {
            Float wt = filter.Evaluate(new Point2f(p - new Point2f(pi) - new Vector2f(0.5f, 0.5f)));
            if (wt != 0)
            {
                ref Pixel pixel = ref pixels[pi];
                for (int i = 0; i < 3; ++i)
                    pixel.rgbSplat[i].Add(wt * rgb[i]);
            }
        }
    }

    public override RGB ToOutputRGB(SampledSpectrum L, in SampledWavelengths lambda)
    {
        RGB cameraRGB = sensor.ToSensorRGB(L, lambda);
        return outputRGBFromSensorRGB * cameraRGB;
    }

    public override bool UsesVisibleSurface() => true;

    public override RGB GetPixelRGB(Point2i p, Float splatScale = 1)
    {
        ref readonly Pixel pixel = ref pixels[p];
        RGB rgb = new((Float)pixel.rgbSum[0], (Float)pixel.rgbSum[1], (Float)pixel.rgbSum[2]);

        // Normalize pixel with weight sum
        Float weightSum = (Float)pixel.weightSum;
        if (weightSum != 0)
            rgb /= weightSum;

        // Add splat value at pixel
        for (int c = 0; c < 3; ++c)
            rgb[c] = (Float)(rgb[c] + splatScale * (double)pixel.rgbSplat[c] / filterIntegral);

        rgb = outputRGBFromSensorRGB * rgb;

        return rgb;
    }

    public override void WriteImage(ImageMetadata metadata, Float splatScale = 1)
    {
        metadata = metadata.Clone();
        Image image = GetImage(metadata, splatScale);
        LOG_VERBOSE($"Writing image {filename} with bounds {pixelBounds}");
        image.Write(filename, metadata);
    }

    public override Image GetImage(ImageMetadata metadata, Float splatScale = 1)
    {
        // Convert image to RGB and compute final pixel values
        LOG_VERBOSE($"Converting image to RGB and computing final weighted pixel values");
        PixelFormat format = writeFP16 ? PixelFormat.Half : PixelFormat.Float;
        Image image = new(format, new Point2i(pixelBounds.Diagonal()),
                          ["R", "G", "B", "Albedo.R", "Albedo.G", "Albedo.B", "P.X", "P.Y", "P.Z", "dzdx", "dzdy",
                           "N.X", "N.Y", "N.Z", "Ns.X", "Ns.Y", "Ns.Z", "u", "v", "Variance.R", "Variance.G",
                           "Variance.B", "RelativeVariance.R", "RelativeVariance.G", "RelativeVariance.B"]);

        ImageChannelDesc rgbDesc = image.GetChannelDesc(["R", "G", "B"]);
        ImageChannelDesc pDesc = image.GetChannelDesc(["P.X", "P.Y", "P.Z"]);
        ImageChannelDesc dzDesc = image.GetChannelDesc(["dzdx", "dzdy"]);
        ImageChannelDesc nDesc = image.GetChannelDesc(["N.X", "N.Y", "N.Z"]);
        ImageChannelDesc nsDesc = image.GetChannelDesc(["Ns.X", "Ns.Y", "Ns.Z"]);
        ImageChannelDesc uvDesc = image.GetChannelDesc(["u", "v"]);
        ImageChannelDesc albedoRgbDesc = image.GetChannelDesc(["Albedo.R", "Albedo.G", "Albedo.B"]);
        ImageChannelDesc varianceDesc = image.GetChannelDesc(["Variance.R", "Variance.G", "Variance.B"]);
        ImageChannelDesc relVarianceDesc =
            image.GetChannelDesc(["RelativeVariance.R", "RelativeVariance.G", "RelativeVariance.B"]);

        int nClamped = 0;
        ParallelFor2D(pixelBounds, (Point2i p) =>
        {
            ref Pixel pixel = ref pixels[p];
            RGB rgb = new((Float)pixel.rgbSum[0], (Float)pixel.rgbSum[1], (Float)pixel.rgbSum[2]);
            RGB albedoRgb = new((Float)pixel.rgbAlbedoSum[0], (Float)pixel.rgbAlbedoSum[1],
                                (Float)pixel.rgbAlbedoSum[2]);

            // Normalize pixel with weight sum
            Float weightSum = (Float)pixel.weightSum, gBufferWeightSum = (Float)pixel.gBufferWeightSum;
            Point3f pt = pixel.pSum;
            Point2f uv = pixel.uvSum;
            Float dzdx = pixel.dzdxSum, dzdy = pixel.dzdySum;
            if (weightSum != 0)
            {
                rgb /= weightSum;
                albedoRgb /= weightSum;
            }
            if (gBufferWeightSum != 0)
            {
                pt /= gBufferWeightSum;
                uv /= gBufferWeightSum;
                dzdx /= gBufferWeightSum;
                dzdy /= gBufferWeightSum;
            }

            // Add splat value at pixel
            for (int c = 0; c < 3; ++c)
                rgb[c] = (Float)(rgb[c] + splatScale * (double)pixel.rgbSplat[c] / filterIntegral);

            rgb = outputRGBFromSensorRGB * rgb;

            if (writeFP16 && FMath.Max(rgb.r, FMath.Max(rgb.g, rgb.b)) > 65504)
            {
                if (rgb.r > 65504)
                    rgb.r = 65504;
                if (rgb.g > 65504)
                    rgb.g = 65504;
                if (rgb.b > 65504)
                    rgb.b = 65504;
                Interlocked.Increment(ref nClamped);
            }

            Point2i pOffset = new(p.x - pixelBounds.pMin.x, p.y - pixelBounds.pMin.y);
            image.SetChannels(pOffset, rgbDesc, [rgb[0], rgb[1], rgb[2]]);
            image.SetChannels(pOffset, albedoRgbDesc, [albedoRgb[0], albedoRgb[1], albedoRgb[2]]);

            Normal3f n = LengthSquared(pixel.nSum) > 0 ? Normalize(pixel.nSum) : new Normal3f(0, 0, 0);
            Normal3f ns = LengthSquared(pixel.nsSum) > 0 ? Normalize(pixel.nsSum) : new Normal3f(0, 0, 0);
            image.SetChannels(pOffset, pDesc, [pt.x, pt.y, pt.z]);
            image.SetChannels(pOffset, dzDesc, [FMath.Abs(dzdx), FMath.Abs(dzdy)]);
            image.SetChannels(pOffset, nDesc, [n.x, n.y, n.z]);
            image.SetChannels(pOffset, nsDesc, [ns.x, ns.y, ns.z]);
            image.SetChannels(pOffset, uvDesc, [uv[0], uv[1]]);
            image.SetChannels(pOffset, varianceDesc,
                              [pixel.rgbVariance[0].Variance(), pixel.rgbVariance[1].Variance(),
                               pixel.rgbVariance[2].Variance()]);
            image.SetChannels(pOffset, relVarianceDesc,
                              [pixel.rgbVariance[0].RelativeVariance(), pixel.rgbVariance[1].RelativeVariance(),
                               pixel.rgbVariance[2].RelativeVariance()]);
        });

        if (nClamped > 0)
            Warning($"{nClamped} pixel values clamped to maximum fp16 value.");

        metadata.pixelBounds = pixelBounds;
        metadata.fullResolution = fullResolution;
        metadata.colorSpace = colorSpace;

        return image;
    }

    public override string ToString() =>
        $"[ GBufferFilm {BaseToString()} outputFromRender: {outputFromRender} applyInverse: {B(applyInverse)} " +
        $"colorSpace: {colorSpace} maxComponentValue: {FloatToString(maxComponentValue)} writeFP16: {B(writeFP16)} ]";

    public static GBufferFilm Create(ParameterDictionary parameters, Float exposureTime,
                                     CameraTransform cameraTransform, Filter filter, RGBColorSpace colorSpace,
                                     FileLoc? loc)
    {
        Float maxComponentValue = parameters.GetOneFloat("maxcomponentvalue", Infinity);
        bool writeFP16 = parameters.GetOneBool("savefp16", true);

        PixelSensor sensor = PixelSensor.Create(parameters, colorSpace, exposureTime, loc);

        FilmBaseParameters filmBaseParameters = new(parameters, filter, sensor, loc);

        if (!HasExtension(filmBaseParameters.filename, "exr"))
            ErrorExit(loc, $"{filmBaseParameters.filename}: EXR is the only format supported by the GBufferFilm.");

        string coordinateSystem = parameters.GetOneString("coordinatesystem", "camera");
        AnimatedTransform outputFromRender = new();
        bool applyInverse = false;
        if (coordinateSystem == "camera")
        {
            outputFromRender = cameraTransform.RenderFromCamera();
            applyInverse = true;
        }
        else if (coordinateSystem == "world")
            outputFromRender = new AnimatedTransform(cameraTransform.WorldFromRender());
        else
            ErrorExit(loc, $"{coordinateSystem}: unknown coordinate system for GBufferFilm. (Expecting \"camera\" or \"world\".)");

        return new GBufferFilm(filmBaseParameters, outputFromRender, applyInverse, colorSpace, maxComponentValue,
                               writeFP16);
    }

    public override void ResetPixel(Point2i p) => pixels[p] = default;
}

// SpectralFilm Definition
public sealed class SpectralFilm : FilmBase
{
    // SpectralFilm::Pixel Definition
    private struct Pixel
    {
        // Continue to store RGB, both to include in the final image as
        // well as for previews during rendering.
        public Double3 rgbSum;
        public double rgbWeightSum;
        public AtomicDouble3 rgbSplat;
    }

    // SpectralFilm Private Members
    private readonly RGBColorSpace colorSpace;
    private readonly Float lambdaMin, lambdaMax;
    private readonly int nBuckets;
    private readonly Float maxComponentValue;
    private readonly bool writeFP16;
    private readonly Float filterIntegral;
    private readonly Array2D<Pixel> pixels;
    private readonly SquareMatrix3 outputRGBFromSensorRGB;
    // (pbrt's per-pixel bucketSums, weightSums and bucketSplats pointers;
    // each pixel has nBuckets entries starting at BucketOffset().)
    private readonly double[] bucketSums, weightSums;
    private readonly AtomicDouble[] bucketSplats;

    // SpectralFilm Public Methods
    public override bool UsesVisibleSurface() => false;

    public override SampledWavelengths SampleWavelengths(Float u) =>
        SampledWavelengths.SampleUniform(u, lambdaMin, lambdaMax);

    private int BucketOffset(Point2i p) =>
        ((p.y - pixelBounds.pMin.y) * (pixelBounds.pMax.x - pixelBounds.pMin.x) + (p.x - pixelBounds.pMin.x)) *
        nBuckets;

    public override void AddSample(Point2i pFilm, SampledSpectrum L, in SampledWavelengths lambda,
                                   in VisibleSurface visibleSurface, Float weight)
    {
        // Start by doing more or less what RGBFilm::AddSample() does so
        // that we can maintain accurate RGB values.

        // Convert sample radiance to _PixelSensor_ RGB
        RGB rgb = sensor.ToSensorRGB(L, lambda);

        // Optionally clamp sensor RGB value
        Float m = FMath.Max(rgb.r, FMath.Max(rgb.g, rgb.b));
        if (m > maxComponentValue)
            rgb *= maxComponentValue / m;

        DCHECK(InsideExclusive(pFilm, pixelBounds));
        // Update RGB fields in Pixel structure.
        ref Pixel pixel = ref pixels[pFilm];
        for (int c = 0; c < 3; ++c)
            pixel.rgbSum[c] += weight * rgb[c];
        pixel.rgbWeightSum += weight;

        // Spectral processing starts here.
        // Optionally clamp spectral value. (TODO: for spectral should we
        // just clamp channels individually?)
        Float lm = L.MaxComponentValue();
        if (lm > maxComponentValue)
            L *= maxComponentValue / lm;

        // The CIE_Y_integral factor effectively cancels out the effect of
        // the conversion of light sources to use photometric units for
        // specification.  We then do *not* divide by the PDF in |lambda|
        // but take advantage of the fact that we know that it is uniform
        // in SampleWavelengths(), the fact that the buckets all have the
        // same extend, and can then just average radiance in buckets
        // below.
        L *= weight * CIE_Y_integral;

        // Accumulate contributions in spectral buckets.
        int offset = BucketOffset(pFilm);
        for (int i = 0; i < NSpectrumSamples; ++i)
        {
            int b = LambdaToBucket(lambda[i]);
            bucketSums[offset + b] += L[i];
            weightSums[offset + b] += weight;
        }
    }

    // SpectralFilm Method Definitions
    public SpectralFilm(FilmBaseParameters p, Float lambdaMin, Float lambdaMax, int nBuckets, RGBColorSpace colorSpace,
                        Float maxComponentValue = Infinity, bool writeFP16 = true)
        : base(p)
    {
        this.colorSpace = colorSpace;
        this.lambdaMin = lambdaMin;
        this.lambdaMax = lambdaMax;
        this.nBuckets = nBuckets;
        this.maxComponentValue = maxComponentValue;
        this.writeFP16 = writeFP16;
        pixels = new Array2D<Pixel>(p.pixelBounds);
        // Compute _outputRGBFromSensorRGB_ matrix
        outputRGBFromSensorRGB = colorSpace.RGBFromXYZ * sensor.XYZFromSensorRGB;

        filterIntegral = filter.Integral();
        CHECK(!pixelBounds.IsEmpty());
        // (sizeof(Pixel) + 3 * nBuckets * sizeof(double) in pbrt)
        filmPixelMemory.Add(pixelBounds.Area() * (80L + 3 * nBuckets * 8));

        // Allocate memory for the pixel buffers in big arrays.
        int nPixels = pixelBounds.Area();
        bucketSums = new double[nBuckets * nPixels];
        weightSums = new double[nBuckets * nPixels];
        bucketSplats = new AtomicDouble[nBuckets * nPixels];
    }

    public override RGB GetPixelRGB(Point2i p, Float splatScale = 1)
    {
        // Note: this is effectively the same as RGBFilm::GetPixelRGB

        ref readonly Pixel pixel = ref pixels[p];
        RGB rgb = new((Float)pixel.rgbSum[0], (Float)pixel.rgbSum[1], (Float)pixel.rgbSum[2]);
        // Normalize _rgb_ with weight sum
        Float weightSum = (Float)pixel.rgbWeightSum;
        if (weightSum != 0)
            rgb /= weightSum;

        // Add splat value at pixel
        for (int c = 0; c < 3; ++c)
            rgb[c] = (Float)(rgb[c] + splatScale * (double)pixel.rgbSplat[c] / filterIntegral);

        // Convert _rgb_ to output RGB color space
        rgb = outputRGBFromSensorRGB * rgb;

        return rgb;
    }

    public override void AddSplat(Point2f p, SampledSpectrum L, in SampledWavelengths lambda)
    {
        // This, too, is similar to RGBFilm::AddSplat(), with additions for
        // spectra.

        CHECK(!L.HasNaNs());

        // Convert sample radiance to _PixelSensor_ RGB
        RGB rgb = sensor.ToSensorRGB(L, lambda);

        // Optionally clamp sensor RGB value
        Float m = FMath.Max(rgb.r, FMath.Max(rgb.g, rgb.b));
        if (m > maxComponentValue)
            rgb *= maxComponentValue / m;

        // Spectral clamping and normalization.
        Float lm = L.MaxComponentValue();
        if (lm > maxComponentValue)
            L *= maxComponentValue / lm;
        L = SafeDiv(L, lambda.PDF()) / NSpectrumSamples;

        // Compute bounds of affected pixels for splat, _splatBounds_
        Bounds2i splatBounds = SplatBounds(p);

        // Splat both RGB and spectral bucket contributions.
        foreach (Point2i pi in splatBounds)
        {
            // Evaluate filter at _pi_ and add splat contribution
            Float wt = filter.Evaluate(new Point2f(p - new Point2f(pi) - new Vector2f(0.5f, 0.5f)));
            if (wt != 0)
            {
                ref Pixel pixel = ref pixels[pi];

                for (int i = 0; i < 3; ++i)
                    pixel.rgbSplat[i].Add(wt * rgb[i]);

                int offset = BucketOffset(pi);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    int b = LambdaToBucket(lambda[i]);
                    bucketSplats[offset + b].Add(wt * L[i]);
                }
            }
        }
    }

    public override void WriteImage(ImageMetadata metadata, Float splatScale = 1)
    {
        metadata = metadata.Clone();
        Image image = GetImage(metadata, splatScale);
        LOG_VERBOSE($"Writing image {filename} with bounds {pixelBounds}");
        image.Write(filename, metadata);
    }

    // Returns an image with both RGB and spectral components, following
    // the layout proposed in "An OpenEXR Layout for Sepctral Images" by
    // Fichet et al., https://jcgt.org/published/0010/03/01/.
    public override Image GetImage(ImageMetadata metadata, Float splatScale = 1)
    {
        // Convert image to RGB and compute final pixel values
        LOG_VERBOSE($"Computing final weighted pixel values");
        PixelFormat format = writeFP16 ? PixelFormat.Half : PixelFormat.Float;

        List<string> imageChannels = ["R", "G", "B"];
        for (int i = 0; i < nBuckets; ++i)
        {
            // The OpenEXR spectral layout takes the bucket center (and then
            // determines bucket widths based on the neighbor wavelengths).
            string lambda = ((double)Lerp((i + 0.5f) / nBuckets, lambdaMin, lambdaMax))
                                .ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "nm";
            // Convert any '.' to ',' in the number since OpenEXR uses '.' for
            // separating layers.
            lambda = lambda.Replace('.', ',');

            imageChannels.Add("S0." + lambda);
        }
        Image image = new(format, new Point2i(pixelBounds.Diagonal()), [.. imageChannels]);

        int nClamped = 0;
        ParallelFor2D(pixelBounds, (Point2i p) =>
        {
            RGB rgb = GetPixelRGB(p, splatScale);

            // Clamp to max representable fp16 to avoid Infs
            if (writeFP16)
            {
                for (int c = 0; c < 3; ++c)
                {
                    if (rgb[c] > 65504)
                    {
                        rgb[c] = 65504;
                        Interlocked.Increment(ref nClamped);
                    }
                }
            }

            Point2i pOffset = new(p.x - pixelBounds.pMin.x, p.y - pixelBounds.pMin.y);
            image.SetChannels(pOffset, [rgb[0], rgb[1], rgb[2]]);

            // Set spectral channels. Hardcoded assuming that they come
            // immediately after RGB, as is currently specified above.
            int offset = BucketOffset(p);
            for (int i = 0; i < nBuckets; ++i)
            {
                Float c = 0;
                if (weightSums[offset + i] > 0)
                {
                    c = (Float)(bucketSums[offset + i] / weightSums[offset + i] +
                                splatScale * (double)bucketSplats[offset + i] / filterIntegral);
                    if (writeFP16 && c > 65504)
                    {
                        c = 65504;
                        Interlocked.Increment(ref nClamped);
                    }
                }
                image.SetChannel(pOffset, 3 + i, c);
            }
        });

        if (nClamped > 0)
            Warning($"{nClamped} pixel values clamped to maximum fp16 value.");

        metadata.pixelBounds = pixelBounds;
        metadata.fullResolution = fullResolution;
        metadata.colorSpace = colorSpace;
        metadata.strings["spectralLayoutVersion"] = "1.0";
        // FIXME: if the RealisticCamera is being used, then we're actually
        // storing "J.m^-2", but that isn't a supported value for
        // "emissiveUnits" in the spec.
        metadata.strings["emissiveUnits"] = "W.m^-2.sr^-1";

        return image;
    }

    public override string ToString() =>
        $"[ SpectralFilm {BaseToString()} lambdaMin: {FloatToString(lambdaMin)} lambdaMax: {FloatToString(lambdaMax)} " +
        $"nBuckets: {nBuckets} writeFP16: {B(writeFP16)} maxComponentValue: {FloatToString(maxComponentValue)} ]";

    public static SpectralFilm Create(ParameterDictionary parameters, Float exposureTime, Filter filter,
                                      RGBColorSpace colorSpace, FileLoc? loc)
    {
        PixelSensor sensor = PixelSensor.Create(parameters, colorSpace, exposureTime, loc);
        FilmBaseParameters filmBaseParameters = new(parameters, filter, sensor, loc);
        bool writeFP16 = parameters.GetOneBool("savefp16", true);

        if (!HasExtension(filmBaseParameters.filename, "exr"))
            ErrorExit(loc, $"{filmBaseParameters.filename}: EXR is the only output format supported by the SpectralFilm.");

        int nBuckets = parameters.GetOneInt("nbuckets", 16);
        Float lambdaMin = parameters.GetOneFloat("lambdamin", Lambda_min);
        Float lambdaMax = parameters.GetOneFloat("lambdamax", Lambda_max);
        if (lambdaMin < Lambda_min || lambdaMax > Lambda_max)
            ErrorExit($"Unfortunately pbrt must be recompiled to render wavelengths beyond the " +
                      $"[{FloatToString(Lambda_min)},{FloatToString(Lambda_max)}] range " +
                      $"([{FloatToString(lambdaMin)},{FloatToString(lambdaMax)}] was specified). Please update " +
                      "Lambda_min and/or Lambda_max as necessary in src/pbrt/util/spectrum.h and recompile.");

        Float maxComponentValue = parameters.GetOneFloat("maxcomponentvalue", Infinity);

        return new SpectralFilm(filmBaseParameters, lambdaMin, lambdaMax, nBuckets, colorSpace, maxComponentValue,
                                writeFP16);
    }

    public override RGB ToOutputRGB(SampledSpectrum L, in SampledWavelengths lambda)
    {
        LOG_FATAL("ToOutputRGB() is unimplemented. But that's ok since it's only used " +
                  "in the SPPM integrator, which is inherently very much based on " +
                  "RGB output.");
        return default;
    }

    public override void ResetPixel(Point2i p)
    {
        pixels[p] = default;
        int offset = BucketOffset(p);
        bucketSums.AsSpan(offset, nBuckets).Clear();
        weightSums.AsSpan(offset, nBuckets).Clear();
        bucketSplats.AsSpan(offset, nBuckets).Clear();
    }

    // SpectralFilm Private Methods
    private int LambdaToBucket(Float lambda)
    {
        int bucket = (int)(nBuckets * (lambda - lambdaMin) / (lambdaMax - lambdaMin));
        return Clamp(bucket, 0, nBuckets - 1);
    }
}

public abstract partial class Film
{
    public static Film Create(string name, ParameterDictionary parameters, Float exposureTime,
                              CameraTransform cameraTransform, Filter filter, FileLoc? loc)
    {
        Film? film = null;
        if (name == "rgb")
            film = RGBFilm.Create(parameters, exposureTime, filter, parameters.ColorSpace()!, loc);
        else if (name == "gbuffer")
            film = GBufferFilm.Create(parameters, exposureTime, cameraTransform, filter, parameters.ColorSpace()!, loc);
        else if (name == "spectral")
            film = SpectralFilm.Create(parameters, exposureTime, filter, parameters.ColorSpace()!, loc);
        else
            ErrorExit(loc, $"{name}: film type unknown.");

        if (film == null)
            ErrorExit(loc, $"{name}: unable to create film.");

        parameters.ReportUnused();
        return film!;
    }
}
