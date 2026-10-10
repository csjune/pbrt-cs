// Derived from FLIP reference implementation: https://research.nvidia.com/sites/default/files/pubs/2020-07_FLIP/flip-cpp.zip

/***************************************************************************
# Copyright (c) 2020, NVIDIA CORPORATION. All rights reserved.
#
# Redistribution and use in source and binary forms, with or without
# modification, are permitted provided that the following conditions
# are met:
#  * Redistributions of source code must retain the above copyright
#    notice, this list of conditions and the following disclaimer.
#  * Redistributions in binary form must reproduce the above copyright
#    notice, this list of conditions and the following disclaimer in the
#    documentation and/or other materials provided with the distribution.
#  * Neither the name of NVIDIA CORPORATION nor the names of its
#    contributors may be used to endorse or promote products derived
#    from this software without specific prior written permission.
#
# THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ``AS IS'' AND ANY
# EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
# IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
# PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT OWNER OR
# CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
# EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
# PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
# PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY
# OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
# (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
# OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
***************************************************************************/

// FLIP: A Difference Evaluator for Alternating Images
// High Performance Graphics, 2020.
// by Pontus Andersson, Jim Nilsson, Tomas Akenine-Moller, Magnus Oskarsson, Kalle Astrom, and Mark D. Fairchild
//
// Pointer to our paper: https://research.nvidia.com/publication/2020-07_FLIP
// code by Pontus Andersson, Jim Nilsson, and Tomas Akenine-Moller
//
// C# port of pbrt-v4's src/ext/flip/flip.cpp, limited to what
// ComputeFLIPError() uses (the histogram/pooling code and the magma color
// map aren't used by imgtool).

namespace Pbrt.ImgTool;

public struct FLIPOptions
{
    public float ppd = 0;
    public float monitorDistance = 0.7f;
    public float monitorWidth = 0.7f;
    public float monitorResolutionX = 3840.0f;

    public FLIPOptions() { }
}

public static class Flip
{
    private static float gPPD;
    private static float gMonitorDistance = 0.7f;
    private static float gMonitorWidth = 0.7f;
    private static float gMonitorResolutionX = 3840.0f;
    private const float gqc = 0.7f;
    private const float gpc = 0.4f;
    private const float gpt = 0.95f;
    private const float gw = 0.082f;
    private const float gqf = 0.5f;

    private const double M_PI = 3.14159265358979323846;

    //  Pixels per degree (PPD)
    private static float calculatePPD(float dist, float resolutionX, float monitorWidth) =>
        dist * (resolutionX / monitorWidth) * ((float)M_PI / 180.0f);

    private struct color3
    {
        public float x, y, z;

        public color3(float _x, float _y, float _z) { x = _x; y = _y; z = _z; }

        public static readonly color3 D65 = new(0.950428545377181f, 1.0f, 1.088900370798128f);

        public color3 abs() { x = MathF.Abs(x); y = MathF.Abs(y); z = MathF.Abs(z); return this; }
        // std::max(x, value) returns x unless x < value; std::min(x, value)
        // returns x unless value < x.
        public color3 max(float value)
        {
            x = (x < value) ? value : x; y = (y < value) ? value : y; z = (z < value) ? value : z;
            return this;
        }
        public color3 min(float value)
        {
            x = (value < x) ? value : x; y = (value < y) ? value : y; z = (value < z) ? value : z;
            return this;
        }

        public color3 sRGB2LinearRGB()
        {
            x = sRGB2Linear(x);
            y = sRGB2Linear(y);
            z = sRGB2Linear(z);
            return this;
        }

        public color3 LinearRGB2XYZ()    // Source: https://www.image-engineering.de/library/technotes/958-how-to-convert-between-srgb-and-ciexyz
        {                                // Assumes D65 standard illuminant
            const float a11 = 10135552.0f / 24577794.0f;
            const float a12 = 8788810.0f / 24577794.0f;
            const float a13 = 4435075.0f / 24577794.0f;
            const float a21 = 2613072.0f / 12288897.0f;
            const float a22 = 8788810.0f / 12288897.0f;
            const float a23 = 887015.0f / 12288897.0f;
            const float a31 = 1425312.0f / 73733382.0f;
            const float a32 = 8788810.0f / 73733382.0f;
            const float a33 = 70074185.0f / 73733382.0f;
            color3 v = this;
            x = a11 * v.x + a12 * v.y + a13 * v.z;
            y = a21 * v.x + a22 * v.y + a23 * v.z;
            z = a31 * v.x + a32 * v.y + a33 * v.z;
            return this;
        }

        public color3 XYZ2LinearRGB()    // Return values in linear RGB, assuming D65 standard illuminant
        {
            const float a11 = 3.241003232976358f;
            const float a12 = -1.537398969488785f;
            const float a13 = -0.498615881996363f;
            const float a21 = -0.969224252202516f;
            const float a22 = 1.875929983695176f;
            const float a23 = 0.041554226340085f;
            const float a31 = 0.055639419851975f;
            const float a32 = -0.204011206123910f;
            const float a33 = 1.057148977187533f;
            color3 v = this;
            x = a11 * v.x + a12 * v.y + a13 * v.z;
            y = a21 * v.x + a22 * v.y + a23 * v.z;
            z = a31 * v.x + a32 * v.y + a33 * v.z;
            return this;
        }

        public color3 XYZ2YCxCz() => XYZ2YCxCz(D65);

        public color3 XYZ2YCxCz(color3 reference_illuminant)     // the default illuminant is D65
        {
            color3 xyz = this;
            xyz.x /= reference_illuminant.x;
            xyz.y /= reference_illuminant.y;
            xyz.z /= reference_illuminant.z;
            float Y = 116.0f * xyz.y - 16.0f;
            float Cx = 500.0f * (xyz.x - xyz.y);
            float Cz = 200.0f * (xyz.y - xyz.z);
            x = Y;
            y = Cx;
            z = Cz;
            return this;
        }

        public color3 YCxCz2XYZ() => YCxCz2XYZ(D65);

        public color3 YCxCz2XYZ(color3 reference_illuminant)     // the default illuminant is D65
        {
            color3 YCxCz = this;
            float Yy = (YCxCz.x + 16.0f) / 116.0f;
            float Cx = YCxCz.y / 500.0f;
            float Cz = YCxCz.z / 200.0f;
            x = Yy + Cx;
            y = Yy;
            z = Yy - Cz;
            x *= reference_illuminant.x;
            y *= reference_illuminant.y;
            z *= reference_illuminant.z;
            return this;
        }

        public color3 XYZ2CIELab() => XYZ2CIELab(D65);

        public color3 XYZ2CIELab(color3 reference_illuminant)     // the default illuminant is D65
        {
            color3 xyz = this;
            xyz.abs();
            xyz.x /= reference_illuminant.x;
            xyz.y /= reference_illuminant.y;
            xyz.z /= reference_illuminant.z;
            // (The comparisons are done in double precision.)
            xyz.x = xyz.x > 0.008856 ? MathF.Pow(xyz.x, 1.0f / 3.0f) : 7.787f * xyz.x + 16.0f / 116.0f;
            xyz.y = xyz.y > 0.008856 ? MathF.Pow(xyz.y, 1.0f / 3.0f) : 7.787f * xyz.y + 16.0f / 116.0f;
            xyz.z = xyz.z > 0.008856 ? MathF.Pow(xyz.z, 1.0f / 3.0f) : 7.787f * xyz.z + 16.0f / 116.0f;
            x = 116.0f * xyz.y - 16.0f;
            y = 500.0f * (xyz.x - xyz.y);
            z = 200.0f * (xyz.y - xyz.z);
            return this;
        }
    }

    // C++'s color3 methods modify the object in place and return a reference
    // to it so that calls can be chained; these do the same for an element
    // of a color3 array.
    private static void Preprocess(ref color3 pixel)
    {
        pixel = pixel.YCxCz2XYZ();
        pixel = pixel.XYZ2LinearRGB();
        pixel = pixel.min(1.0f);
        pixel = pixel.max(0.0f);
        pixel = pixel.LinearRGB2XYZ();
        pixel = pixel.XYZ2CIELab();
    }

    private static float sRGB2Linear(float sRGBColor)
    {
        if (sRGBColor <= 0.04045f)
            return sRGBColor / 12.92f;
        else
            return MathF.Pow((sRGBColor + 0.055f) / 1.055f, 2.4f);
    }

    private static float HyAB(in color3 refPixel, in color3 testPixel)
    {
        float cityBlockDistanceL = MathF.Abs(refPixel.x - testPixel.x);
        float euclideanDistanceAB = MathF.Sqrt((refPixel.y - testPixel.y) * (refPixel.y - testPixel.y) +
                                               (refPixel.z - testPixel.z) * (refPixel.z - testPixel.z));
        return cityBlockDistanceL + euclideanDistanceAB;
    }

    private static float Gaussian(float x, float y, float sigma) =>
        MathF.Exp(-(x * x + y * y) / (2.0f * sigma * sigma));

    private sealed class FlipImage
    {
        public int mWidth = 0;
        public int mHeight = 0;
        public color3[] mPixels = [];

        public FlipImage() { }
        public FlipImage(int width, int height) => allocate(width, height);

        public FlipImage Clone() => new() { mWidth = mWidth, mHeight = mHeight, mPixels = (color3[])mPixels.Clone() };

        public void allocate(int width, int height)
        {
            mWidth = width;
            mHeight = height;
            // (std::vector::resize() keeps the existing values.)
            color3[] pixels = new color3[mWidth * mHeight];
            Array.Copy(mPixels, pixels, Math.Min(mPixels.Length, pixels.Length));
            mPixels = pixels;
        }

        public color3 get(int x, int y) => mPixels[y * mWidth + x];
        public void set(int x, int y, color3 color) => mPixels[y * mWidth + x] = color;

        private static float GaussSum(float x2, float a1, float b1, float a2, float b2)
        {
            const float pi = (float)M_PI;
            const float pi_sq = (float)(M_PI * M_PI);
            return a1 * MathF.Sqrt(pi / b1) * MathF.Exp(-pi_sq * x2 / b1) +
                   a2 * MathF.Sqrt(pi / b2) * MathF.Exp(-pi_sq * x2 / b2);
        }

        public void generateSpatialFilter(float ppd)
        {
            float deltaX = 1.0f / ppd;
            const float pi_sq = (float)(M_PI * M_PI);
            // constants for Gaussians -- see paper for details.
            color3 a1 = new(1.0f, 1.0f, 34.1f);
            color3 b1 = new(0.0047f, 0.0053f, 0.04f);
            color3 a2 = new(0.0f, 0.0f, 13.5f);
            color3 b2 = new(1.0e-5f, 1.0e-5f, 0.025f);

            float maxScaleParameter = Math.Max(Math.Max(Math.Max(b1.x, b1.y), Math.Max(b1.z, b2.x)), Math.Max(b2.y, b2.z));
            int radius = (int)MathF.Ceiling(3.0f * MathF.Sqrt(maxScaleParameter / (2.0f * pi_sq)) * ppd); // Set radius based on largest scale parameter

            int width = 2 * radius + 1;
            allocate(width, width);

            color3 filterSum = new(0.0f, 0.0f, 0.0f);
            for (int y = 0; y < width; y++)
            {
                float iy = (y - radius) * deltaX;
                for (int x = 0; x < width; x++)
                {
                    float ix = (x - radius) * deltaX;

                    float dist2 = ix * ix + iy * iy;
                    color3 value = new(GaussSum(dist2, a1.x, b1.x, a2.x, b2.x), GaussSum(dist2, a1.y, b1.y, a2.y, b2.y),
                                       GaussSum(dist2, a1.z, b1.z, a2.z, b2.z));
                    set(x, y, value);
                    filterSum = new color3(filterSum.x + value.x, filterSum.y + value.y, filterSum.z + value.z);
                }
            }

            // normalize weights
            for (int y = 0; y < width; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    color3 value = get(x, y);
                    set(x, y, new color3(value.x / filterSum.x, value.y / filterSum.y, value.z / filterSum.z));
                }
            }
        }

        // assumes a filter whose weights sum to one, and we use "replicate"
        // for borders, i.e., clamp to borders
        public void convolve(FlipImage unfilteredImage, FlipImage filter)
        {
            int halfFilterWidth = filter.mWidth / 2;
            int halfFilterHeight = filter.mHeight / 2;

            // apply filter
            for (int y = 0; y < mHeight; y++)
            {
                for (int x = 0; x < mWidth; x++)
                {
                    color3 colorSum = new(0.0f, 0.0f, 0.0f);

                    // filter with the kernel
                    for (int iy = -halfFilterHeight; iy <= halfFilterHeight; iy++)
                    {
                        int yy = Math.Min(Math.Max(0, y + iy), mHeight - 1);
                        for (int ix = -halfFilterWidth; ix <= halfFilterWidth; ix++)
                        {
                            int xx = Math.Min(Math.Max(0, x + ix), mWidth - 1);
                            // read from unfilteredImage and sum into colorSum and weightSum
                            color3 weights = filter.get(ix + halfFilterWidth, iy + halfFilterHeight);
                            color3 srcColor = unfilteredImage.get(xx, yy);
                            colorSum = new color3(colorSum.x + weights.x * srcColor.x, colorSum.y + weights.y * srcColor.y,
                                                  colorSum.z + weights.z * srcColor.z);
                        }
                    }
                    set(x, y, new color3(colorSum.x, colorSum.y, colorSum.z));     // compute final filtered value (and store in mPixels)
                }
            }
        }

        public void huntAdjustment()
        {
            for (int i = 0; i < mPixels.Length; ++i)
            {
                ref color3 pixel = ref mPixels[i];
                pixel.y = Hunt(pixel.x, pixel.y);
                pixel.z = Hunt(pixel.x, pixel.z);
            }
        }

        // implements spatial filter and Hunt adjustment
        public void preprocess(FlipImage filter)
        {
            // convolve with CSF filters
            FlipImage inputImage = Clone();              // copy the input image
            convolve(inputImage, filter);

            // the result after the spatial filter is in YCxCz. Clamp and
            // transform this->mPixels to L*a*b
            for (int i = 0; i < mPixels.Length; ++i)
                Preprocess(ref mPixels[i]);

            huntAdjustment();
        }

        public void computeColorDifference(FlipImage preprocessedReference, FlipImage preprocessedTest)
        {
            allocate(preprocessedReference.mWidth, preprocessedReference.mHeight);

            float cmax = computeMaxDistance();
            float pccmax = gpc * cmax;

            // compute difference in HyAB and redistribute errors
            for (int y = 0; y < mHeight; y++)
            {
                for (int x = 0; x < mWidth; x++)
                {
                    // compute difference in HyAB
                    color3 refPixel = preprocessedReference.get(x, y);
                    color3 testPixel = preprocessedTest.get(x, y);
                    float error = HyAB(refPixel, testPixel);

                    error = MathF.Pow(error, gqc);

                    // Re-map error to the [0, 1] range. Values between 0 and
                    // pccmax are mapped to the range [0, gpt], while the rest
                    // are mapped to the range (gpt, 1]
                    if (error < pccmax)
                        error *= gpt / pccmax;
                    else
                        error = gpt + ((error - pccmax) / (cmax - pccmax)) * (1.0f - gpt);

                    set(x, y, new color3(error, 0.0f, 0.0f));
                }
            }
        }

        public void generateDetectionFilters(float ppd, bool pointDetector)
        {
            float stdDev = 0.5f * gw * ppd;
            int radius = (int)MathF.Ceiling(3.0f * stdDev);
            int width = 2 * radius + 1;

            allocate(width, width);

            float weightX, weightY;
            float negativeWeightsSumX = 0.0f;
            float positiveWeightsSumX = 0.0f;
            float negativeWeightsSumY = 0.0f;
            float positiveWeightsSumY = 0.0f;

            for (int y = 0; y < width; y++)
            {
                int yy = y - radius;
                for (int x = 0; x < width; x++)
                {
                    int xx = x - radius;
                    float G = Gaussian((float)xx, (float)yy, stdDev);
                    if (pointDetector)
                    {
                        weightX = ((float)xx * (float)xx / (stdDev * stdDev) - 1.0f) * G;
                        weightY = ((float)yy * (float)yy / (stdDev * stdDev) - 1.0f) * G;
                    }
                    else
                    {
                        weightX = -(float)xx * G;
                        weightY = -(float)yy * G;
                    }
                    set(x, y, new color3(weightX, weightY, 0.0f));

                    if (weightX > 0.0f)
                        positiveWeightsSumX += weightX;
                    else
                        negativeWeightsSumX += -weightX;

                    if (weightY > 0.0f)
                        positiveWeightsSumY += weightY;
                    else
                        negativeWeightsSumY += -weightY;
                }
            }

            // Normalize positive weights to sum to 1 and negative weights to sum to -1
            for (int y = 0; y < width; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    color3 p = get(x, y);

                    set(x, y, new color3(p.x / (p.x > 0.0f ? positiveWeightsSumX : negativeWeightsSumX),
                                         p.y / (p.y > 0.0f ? positiveWeightsSumY : negativeWeightsSumY), 0.0f));
                }
            }
        }

        public void computeFeatureDifference(FlipImage refImage, FlipImage testImage, float ppd)
        {
            allocate(refImage.mWidth, testImage.mHeight);

            // make copy of ref and test images and extract and normalize achromatic component
            FlipImage refGray = refImage.Clone();
            FlipImage testGray = testImage.Clone();
            for (int i = 0; i < refGray.mPixels.Length; ++i)
            {
                float c = (refGray.mPixels[i].x + 16.0f) / 116.0f;   // make it [0,1]
                refGray.mPixels[i] = new color3(c, c, 0.0f);         // luminance [0,1] stored in both x and y since we apply both horizontal and verticals filters at the same time
            }

            for (int i = 0; i < testGray.mPixels.Length; ++i)
            {
                float c = (testGray.mPixels[i].x + 16.0f) / 116.0f;  // make it [0,1]
                testGray.mPixels[i] = new color3(c, c, 0.0f);        // luminance [0,1] stored in both x and y since we apply both horizontal and verticals filters at the same time
            }

            FlipImage edgesReference = new(mWidth, mHeight);
            FlipImage pointsReference = new(mWidth, mHeight);
            FlipImage edgesTest = new(mWidth, mHeight);
            FlipImage pointsTest = new(mWidth, mHeight);

            FlipImage edgeFilters = new();
            FlipImage pointFilters = new();
            edgeFilters.generateDetectionFilters(ppd, false); // contains both horiontal and vertical filter
            pointFilters.generateDetectionFilters(ppd, true); // contains both horiontal and vertical filter

            edgesReference.convolve(refGray, edgeFilters);
            edgesTest.convolve(testGray, edgeFilters);
            pointsReference.convolve(refGray, pointFilters);
            pointsTest.convolve(testGray, pointFilters);

            float normalizationFactor = 1.0f / MathF.Sqrt(2.0f);
            color3 p;
            for (int y = 0; y < mHeight; y++)
            {
                for (int x = 0; x < mWidth; x++)
                {
                    p = edgesReference.get(x, y);
                    float edgeValueRef = MathF.Sqrt(p.x * p.x + p.y * p.y);
                    p = edgesTest.get(x, y);
                    float edgeValueTest = MathF.Sqrt(p.x * p.x + p.y * p.y);
                    p = pointsReference.get(x, y);
                    float pointValueRef = MathF.Sqrt(p.x * p.x + p.y * p.y);
                    p = pointsTest.get(x, y);
                    float pointValueTest = MathF.Sqrt(p.x * p.x + p.y * p.y);

                    float edgeDifference = MathF.Abs(edgeValueRef - edgeValueTest);
                    float pointDifference = MathF.Abs(pointValueRef - pointValueTest);

                    // (std::max(a, b) is b only if a < b.)
                    float featureDifference = MathF.Pow(
                        normalizationFactor * (edgeDifference < pointDifference ? pointDifference : edgeDifference), gqf);

                    set(x, y, new color3(featureDifference, 0.0f, 0.0f));
                }
            }
        }

        public void computeFLIPError(FlipImage refImage, FlipImage testImage)
        {
            // Transform refImage and testImage to YCxCz opponent space
            for (int i = 0; i < refImage.mPixels.Length; ++i)
                refImage.mPixels[i] = refImage.mPixels[i].sRGB2LinearRGB().LinearRGB2XYZ().XYZ2YCxCz();
            for (int i = 0; i < testImage.mPixels.Length; ++i)
                testImage.mPixels[i] = testImage.mPixels[i].sRGB2LinearRGB().LinearRGB2XYZ().XYZ2YCxCz();

            // Color pipeline
            // Preprocess images
            FlipImage preprocessedReference = refImage.Clone();
            FlipImage preprocessedTest = testImage.Clone();
            FlipImage filter = new();
            filter.generateSpatialFilter(gPPD);
            preprocessedReference.preprocess(filter);
            preprocessedTest.preprocess(filter);
            // Compute color difference
            FlipImage colorDifference = new();
            colorDifference.computeColorDifference(preprocessedReference, preprocessedTest);

            // Feature pipeline
            FlipImage featureDifference = new();
            featureDifference.computeFeatureDifference(refImage, testImage, gPPD);

            // Final error
            allocate(colorDifference.mWidth, colorDifference.mHeight);
            for (int y = 0; y < mHeight; y++)
            {
                for (int x = 0; x < mWidth; x++)
                {
                    float cdiff = colorDifference.get(x, y).x;
                    float fdiff = featureDifference.get(x, y).x;
                    float errorFLIP = MathF.Pow(cdiff, 1.0f - fdiff);
                    set(x, y, new color3(errorFLIP, errorFLIP, errorFLIP));
                }
            }
        }
    }

    private static float Hunt(float luminance, float chrominance) => 0.01f * luminance * chrominance;

    private static float computeMaxDistance()
    {
        color3 greenLab = new(0.0f, 1.0f, 0.0f);     // in RGB to start with
        color3 blueLab = new(0.0f, 0.0f, 1.0f);
        greenLab = greenLab.LinearRGB2XYZ().XYZ2CIELab();          // now in Lab
        blueLab = blueLab.LinearRGB2XYZ().XYZ2CIELab();
        color3 greenLabHunt = new(greenLab.x, Hunt(greenLab.x, greenLab.y), Hunt(greenLab.x, greenLab.z));
        color3 blueLabHunt = new(blueLab.x, Hunt(blueLab.x, blueLab.y), Hunt(blueLab.x, blueLab.z));

        return MathF.Pow(HyAB(greenLabHunt, blueLabHunt), gqc);
    }

    public static void ComputeFLIPError(ReadOnlySpan<float> testRGB, ReadOnlySpan<float> referenceRGB,
                                        Span<float> flipError, int xRes, int yRes, in FLIPOptions options)
    {
        if (options.ppd > 0)
            gPPD = options.ppd;
        else
        {
            gMonitorDistance = options.monitorDistance;
            gMonitorWidth = options.monitorWidth;
            gMonitorResolutionX = options.monitorResolutionX;
            gPPD = calculatePPD(gMonitorDistance, gMonitorResolutionX, gMonitorWidth);
        }

        FlipImage testImage = new(xRes, yRes), referenceImage = new(xRes, yRes), flipImage = new(xRes, yRes);
        for (int y = 0; y < yRes; ++y)
            for (int x = 0; x < xRes; ++x)
            {
                int offset = 3 * (x + y * xRes);
                testImage.set(x, y, new color3(testRGB[offset], testRGB[offset + 1], testRGB[offset + 2]));
                referenceImage.set(x, y, new color3(referenceRGB[offset], referenceRGB[offset + 1], referenceRGB[offset + 2]));
            }

        // (As in the C++, the test FlipImage is passed as computeFLIPError()'s
        // refImage parameter and vice versa.)
        flipImage.computeFLIPError(testImage, referenceImage);

        for (int y = 0; y < yRes; ++y)
            for (int x = 0; x < xRes; ++x)
                flipError[x + y * xRes] = flipImage.get(x, y).x;
    }
}
