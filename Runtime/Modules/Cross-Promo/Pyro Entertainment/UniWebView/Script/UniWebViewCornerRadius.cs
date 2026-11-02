//
//  UniWebViewCornerRadius.cs
//  Created by Wang Wei(@onevcat) on 2025-10-26.
//
//  This file is a part of UniWebView Project (https://uniwebview.com)
//  By purchasing the asset, you are allowed to use this code in as many as projects 
//  you want, only if you publish the final products under the name of the same account
//  used for the purchase. 
//
//  This asset and all corresponding files (such as source code) are provided on an 
//  “as is” basis, without warranty of any kind, express of implied, including but not
//  limited to the warranties of merchantability, fitness for a particular purpose, and 
//  noninfringement. In no event shall the authors or copyright holders be liable for any 
//  claim, damages or other liability, whether in action of contract, tort or otherwise, 
//  arising from, out of or in connection with the software or the use of other dealing in the software.
//

using UnityEngine;

/// <summary>
/// Represents the corner radii of a UniWebView instance.
/// </summary>
public readonly struct UniWebViewCornerRadius {
    /// <summary>
    /// Corner radius at the top-left corner.
    /// </summary>
    public float TopLeft { get; }

    /// <summary>
    /// Corner radius at the top-right corner.
    /// </summary>
    public float TopRight { get; }

    /// <summary>
    /// Corner radius at the bottom-left corner.
    /// </summary>
    public float BottomLeft { get; }

    /// <summary>
    /// Corner radius at the bottom-right corner.
    /// </summary>
    public float BottomRight { get; }

    /// <summary>
    /// Creates a corner radius with individual values for each corner.
    /// </summary>
    public UniWebViewCornerRadius(float topLeft, float topRight, float bottomLeft, float bottomRight) {
        TopLeft = Mathf.Max(0.0f, topLeft);
        TopRight = Mathf.Max(0.0f, topRight);
        BottomLeft = Mathf.Max(0.0f, bottomLeft);
        BottomRight = Mathf.Max(0.0f, bottomRight);
    }

    /// <summary>
    /// Creates a corner radius using the same value for all corners.
    /// </summary>
    public static UniWebViewCornerRadius Uniform(float radius) {
        return new UniWebViewCornerRadius(radius, radius, radius, radius);
    }

    /// <summary>
    /// A corner radius with all corners set to zero.
    /// </summary>
    public static UniWebViewCornerRadius Zero => new UniWebViewCornerRadius(0.0f, 0.0f, 0.0f, 0.0f);

    /// <summary>
    /// Determines whether all corners are zero.
    /// </summary>
    public bool IsZero =>
        Mathf.Approximately(TopLeft, 0.0f) &&
        Mathf.Approximately(TopRight, 0.0f) &&
        Mathf.Approximately(BottomLeft, 0.0f) &&
        Mathf.Approximately(BottomRight, 0.0f);
}
