/*
This source is published under the following 3-clause BSD license.

Copyright (c) 2012 - 2013, Lukas Hosek and Alexander Wilkie
All rights reserved.

Redistribution and use in source and binary forms, with or without 
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.
    * None of the names of the contributors may be used to endorse or promote 
      products derived from this software without specific prior written 
      permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDERS BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

// C# port of the spectral parts of pbrt-v4's src/ext/skymodel/ArHosekSkyModel.c
// (version 1.4a), which is all that imgtool's makesky uses: the terrestrial
// arhosekskymodelstate_alloc_init() and arhosekskymodel_solar_radiance().

using static Pbrt.ImgTool.SkyModel.ArHosekSkyModelData;

namespace Pbrt.ImgTool.SkyModel;

public sealed class ArHosekSkyModelState
{
    public readonly double[][] configs = new double[11][];
    public readonly double[] radiances = new double[11];
    public double turbidity;
    public double solar_radius;
    public readonly double[] emission_correction_factor_sky = new double[11];
    public readonly double[] emission_correction_factor_sun = new double[11];
    public double albedo;
    public double elevation;

    public ArHosekSkyModelState()
    {
        for (int i = 0; i < 11; ++i)
            configs[i] = new double[9];
    }
}

public static class ArHosekSkyModel
{
    private const double MATH_PI = 3.141592653589793;
    private const double MATH_DEG_TO_RAD = MATH_PI / 180.0;

    // The Bernstein polynomial blend of the six elevation control points
    // that the C code writes out for each albedo/turbidity combination.
    private static double Blend(double[] elev_matrix, int offset, int i, int stride, double solar_elevation) =>
        Math.Pow(1.0 - solar_elevation, 5.0) * elev_matrix[offset + i] +
        5.0 * Math.Pow(1.0 - solar_elevation, 4.0) * solar_elevation * elev_matrix[offset + i + stride] +
        10.0 * Math.Pow(1.0 - solar_elevation, 3.0) * Math.Pow(solar_elevation, 2.0) * elev_matrix[offset + i + 2 * stride] +
        10.0 * Math.Pow(1.0 - solar_elevation, 2.0) * Math.Pow(solar_elevation, 3.0) * elev_matrix[offset + i + 3 * stride] +
        5.0 * (1.0 - solar_elevation) * Math.Pow(solar_elevation, 4.0) * elev_matrix[offset + i + 4 * stride] +
        Math.Pow(solar_elevation, 5.0) * elev_matrix[offset + i + 5 * stride];

    private static void ArHosekSkyModel_CookConfiguration(double[] dataset, double[] config, double turbidity,
                                                          double albedo, double solar_elevation)
    {
        int int_turbidity = (int)turbidity;
        double turbidity_rem = turbidity - (double)int_turbidity;

        solar_elevation = Math.Pow(solar_elevation / (MATH_PI / 2.0), (1.0 / 3.0));

        // alb 0 low turb
        int elev_matrix = 9 * 6 * (int_turbidity - 1);
        for (int i = 0; i < 9; ++i)
        {
            //(1-t).^3* A1 + 3*(1-t).^2.*t * A2 + 3*(1-t) .* t .^ 2 * A3 + t.^3 * A4;
            config[i] = (1.0 - albedo) * (1.0 - turbidity_rem) * Blend(dataset, elev_matrix, i, 9, solar_elevation);
        }

        // alb 1 low turb
        elev_matrix = 9 * 6 * 10 + 9 * 6 * (int_turbidity - 1);
        for (int i = 0; i < 9; ++i)
            config[i] += (albedo) * (1.0 - turbidity_rem) * Blend(dataset, elev_matrix, i, 9, solar_elevation);

        if (int_turbidity == 10)
            return;

        // alb 0 high turb
        elev_matrix = 9 * 6 * (int_turbidity);
        for (int i = 0; i < 9; ++i)
            config[i] += (1.0 - albedo) * (turbidity_rem) * Blend(dataset, elev_matrix, i, 9, solar_elevation);

        // alb 1 high turb
        elev_matrix = 9 * 6 * 10 + 9 * 6 * (int_turbidity);
        for (int i = 0; i < 9; ++i)
            config[i] += (albedo) * (turbidity_rem) * Blend(dataset, elev_matrix, i, 9, solar_elevation);
    }

    private static double ArHosekSkyModel_CookRadianceConfiguration(double[] dataset, double turbidity, double albedo,
                                                                    double solar_elevation)
    {
        int int_turbidity = (int)turbidity;
        double turbidity_rem = turbidity - (double)int_turbidity;
        double res;
        solar_elevation = Math.Pow(solar_elevation / (MATH_PI / 2.0), (1.0 / 3.0));

        // alb 0 low turb
        int elev_matrix = 6 * (int_turbidity - 1);
        //(1-t).^3* A1 + 3*(1-t).^2.*t * A2 + 3*(1-t) .* t .^ 2 * A3 + t.^3 * A4;
        res = (1.0 - albedo) * (1.0 - turbidity_rem) * Blend(dataset, elev_matrix, 0, 1, solar_elevation);

        // alb 1 low turb
        elev_matrix = 6 * 10 + 6 * (int_turbidity - 1);
        res += (albedo) * (1.0 - turbidity_rem) * Blend(dataset, elev_matrix, 0, 1, solar_elevation);
        if (int_turbidity == 10)
            return res;

        // alb 0 high turb
        elev_matrix = 6 * (int_turbidity);
        res += (1.0 - albedo) * (turbidity_rem) * Blend(dataset, elev_matrix, 0, 1, solar_elevation);

        // alb 1 high turb
        elev_matrix = 6 * 10 + 6 * (int_turbidity);
        res += (albedo) * (turbidity_rem) * Blend(dataset, elev_matrix, 0, 1, solar_elevation);
        return res;
    }

    private static double ArHosekSkyModel_GetRadianceInternal(double[] configuration, double theta, double gamma)
    {
        double expM = Math.Exp(configuration[4] * gamma);
        double rayM = Math.Cos(gamma) * Math.Cos(gamma);
        double mieM = (1.0 + Math.Cos(gamma) * Math.Cos(gamma)) /
                      Math.Pow((1.0 + configuration[8] * configuration[8] - 2.0 * configuration[8] * Math.Cos(gamma)), 1.5);
        double zenith = Math.Sqrt(Math.Cos(theta));

        return (1.0 + configuration[0] * Math.Exp(configuration[1] / (Math.Cos(theta) + 0.01))) *
               (configuration[2] + configuration[3] * expM + configuration[5] * rayM + configuration[6] * mieM +
                configuration[7] * zenith);
    }

    // spectral version
    public static ArHosekSkyModelState arhosekskymodelstate_alloc_init(double solar_elevation,
                                                                       double atmospheric_turbidity,
                                                                       double ground_albedo)
    {
        ArHosekSkyModelState state = new();

        state.solar_radius = (0.51 * MATH_DEG_TO_RAD) / 2.0;
        state.turbidity = atmospheric_turbidity;
        state.albedo = ground_albedo;
        state.elevation = solar_elevation;

        for (int wl = 0; wl < 11; ++wl)
        {
            ArHosekSkyModel_CookConfiguration(datasets[wl], state.configs[wl], atmospheric_turbidity, ground_albedo,
                                              solar_elevation);

            state.radiances[wl] = ArHosekSkyModel_CookRadianceConfiguration(datasetsRad[wl], atmospheric_turbidity,
                                                                            ground_albedo, solar_elevation);

            state.emission_correction_factor_sun[wl] = 1.0;
            state.emission_correction_factor_sky[wl] = 1.0;
        }

        return state;
    }

    public static double arhosekskymodel_radiance(ArHosekSkyModelState state, double theta, double gamma,
                                                  double wavelength)
    {
        int low_wl = (int)((wavelength - 320.0) / 40.0);

        if (low_wl < 0 || low_wl >= 11)
            return 0.0f;

        // (C# % on doubles is fmod().)
        double interp = ((wavelength - 320.0) / 40.0) % 1.0;

        double val_low = ArHosekSkyModel_GetRadianceInternal(state.configs[low_wl], theta, gamma) *
                         state.radiances[low_wl] * state.emission_correction_factor_sky[low_wl];

        if (interp < 1e-6)
            return val_low;

        double result = (1.0 - interp) * val_low;

        if (low_wl + 1 < 11)
        {
            result += interp * ArHosekSkyModel_GetRadianceInternal(state.configs[low_wl + 1], theta, gamma) *
                      state.radiances[low_wl + 1] * state.emission_correction_factor_sky[low_wl + 1];
        }

        return result;
    }

    private const int pieces = 45;
    private const int order = 4;

    private static double arhosekskymodel_sr_internal(ArHosekSkyModelState state, int turbidity, int wl,
                                                      double elevation)
    {
        int pos = (int)(Math.Pow(2.0 * elevation / MATH_PI, 1.0 / 3.0) * pieces); // floor

        if (pos > 44) pos = 44;

        double break_x = Math.Pow(((double)pos / (double)pieces), 3.0) * (MATH_PI * 0.5);

        double[] coefs = solarDatasets[wl];
        int c = order * pieces * turbidity + order * (pos + 1) - 1;

        double res = 0.0;
        double x = elevation - break_x;
        double x_exp = 1.0;

        for (int i = 0; i < order; ++i)
        {
            res += x_exp * coefs[c--];
            x_exp *= x;
        }

        return res * state.emission_correction_factor_sun[wl];
    }

    private static double arhosekskymodel_solar_radiance_internal2(ArHosekSkyModelState state, double wavelength,
                                                                   double elevation, double gamma)
    {
        // sun distance to diameter ratio, squared
        double sol_rad_sin = Math.Sin(state.solar_radius);
        double ar2 = 1 / (sol_rad_sin * sol_rad_sin);
        double singamma = Math.Sin(gamma);
        double sc2 = 1.0 - ar2 * singamma * singamma;
        if (sc2 < 0.0) sc2 = 0.0;
        double sampleCosine = Math.Sqrt(sc2);

        if (sampleCosine == 0.0) return 0.0;

        int turb_low = (int)state.turbidity - 1;
        double turb_frac = state.turbidity - (double)(turb_low + 1);

        if (turb_low == 9)
        {
            turb_low = 8;
            turb_frac = 1.0;
        }

        int wl_low = (int)((wavelength - 320.0) / 40.0);
        double wl_frac = (wavelength % 40.0) / 40.0;

        if (wl_low == 10)
        {
            wl_low = 9;
            wl_frac = 1.0;
        }

        double direct_radiance =
            (1.0 - turb_frac) * ((1.0 - wl_frac) * arhosekskymodel_sr_internal(state, turb_low, wl_low, elevation) +
                                 wl_frac * arhosekskymodel_sr_internal(state, turb_low, wl_low + 1, elevation)) +
            turb_frac * ((1.0 - wl_frac) * arhosekskymodel_sr_internal(state, turb_low + 1, wl_low, elevation) +
                         wl_frac * arhosekskymodel_sr_internal(state, turb_low + 1, wl_low + 1, elevation));

        Span<double> ldCoefficient = stackalloc double[6];

        for (int i = 0; i < 6; i++)
            ldCoefficient[i] = (1.0 - wl_frac) * limbDarkeningDatasets[wl_low][i] +
                               wl_frac * limbDarkeningDatasets[wl_low + 1][i];

        // The following will be improved in future versions of the model:
        // here, we directly use fitted 5th order polynomials provided by the
        // astronomical community for the limb darkening effect. Astronomers need
        // such accurate fittings for their predictions. However, this sort of
        // accuracy is not really needed for CG purposes, so an approximated
        // dataset based on quadratic polynomials will be provided in a future
        // release.
        double darkeningFactor = ldCoefficient[0] + ldCoefficient[1] * sampleCosine +
                                 ldCoefficient[2] * Math.Pow(sampleCosine, 2.0) +
                                 ldCoefficient[3] * Math.Pow(sampleCosine, 3.0) +
                                 ldCoefficient[4] * Math.Pow(sampleCosine, 4.0) +
                                 ldCoefficient[5] * Math.Pow(sampleCosine, 5.0);

        direct_radiance *= darkeningFactor;

        return direct_radiance;
    }

    public static double arhosekskymodel_solar_radiance(ArHosekSkyModelState state, double theta, double gamma,
                                                        double wavelength)
    {
        double direct_radiance =
            arhosekskymodel_solar_radiance_internal2(state, wavelength, ((MATH_PI / 2.0) - theta), gamma);

        double inscattered_radiance = arhosekskymodel_radiance(state, theta, gamma, wavelength);

        return direct_radiance + inscattered_radiance;
    }
}
