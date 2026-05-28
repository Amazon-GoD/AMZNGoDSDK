//
//  UniWebViewTransform.cs
//  Created by Wang Wei(@onevcat) on 2025-03-17.
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

/// <summary>
/// Represents a transformation that can be applied to a UniWebView, including rotation and scaling.
/// 
/// The transformation includes:
/// 
/// - **Rotation**: The angle (in degrees) by which the web view should be rotated. Positive values rotate the web
/// view clockwise, while negative values rotate it counterclockwise.
/// - **Scaling**: The scale factors for the X and Y axes. A scale factor of 1.0 means no scaling, while values
/// greater than 1.0 enlarge the web view and values less than 1.0 shrink it.
/// 
/// This transformation is typically used in conjunction with the `SetTransform` method in `UniWebView` to apply visual
/// transformations to the web view.
/// </summary>
public class UniWebViewTransform {
    /// <summary>
    /// The rotation of the web view in degrees.
    /// </summary>
    public float Rotation { get; set; }

    /// <summary>
    /// The scaling factor applied to the X-axis of the web view.
    /// </summary>
    public float ScaleX { get; set;  }

    /// <summary>
    /// The scaling factor applied to the Y-axis of the web view.
    /// </summary>
    public float ScaleY { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="UniWebViewTransform"/> class with the specified rotation and
    /// scaling factors.
    /// </summary>
    /// <param name="rotation">The rotation of the web view in degrees.</param>
    /// <param name="scaleX">The scaling factor applied to the X-axis of the web view. Default is 1.0.</param>
    /// <param name="scaleY">The scaling factor applied to the Y-axis of the web view. Default is 1.0.</param>
    public UniWebViewTransform(float rotation, float scaleX = 1.0f, float scaleY = 1.0f) {
        Rotation = rotation;
        ScaleX = scaleX;
        ScaleY = scaleY;
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="UniWebViewTransform"/> class with the specified scaling factors.
    /// </summary>
    /// <param name="scaleX">The scaling factor applied to the X-axis of the web view. Default is 1.0.</param>
    /// <param name="scaleY">The scaling factor applied to the Y-axis of the web view. Default is 1.0.</param>
    public UniWebViewTransform(float scaleX, float scaleY) {
        Rotation = 0.0f;
        ScaleX = scaleX;
        ScaleY = scaleY;
    }
}