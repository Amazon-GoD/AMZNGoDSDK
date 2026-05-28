//
//  UniWebView.Snapshot.cs
//  Created by Wang Wei (@onevcat) on 2025-04-19.
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
using System;
using System.Threading.Tasks;

public partial class UniWebView {
    private static readonly Rect snapshotFullViewRect = new Rect(-1, -1, -1, -1);
    
    /// <summary>
    /// Capture the content of web view and store it to the cache path on disk with the given file name.
    /// 
    /// When the capturing finishes, `OnCaptureSnapshotFinished` event will be raised, with an error code to indicate
    /// whether the operation succeeded and an accessible disk path of the image. 
    /// 
    /// The captured image will be stored as a PNG file under the `fileName` in app's cache folder. If a file with the 
    /// same file name already exists, it will be overridden by the new captured image.
    /// </summary>
    /// <param name="fileName">
    /// The file name to which the captured image is stored to, for example "screenshot.png". If empty, UniWebView will
    /// pick a random UUID with "png" file extension as the file name.
    /// </param>
    public void CaptureSnapshot(string fileName) {
        UniWebViewInterface.CaptureSnapshot(listener.Name, fileName);
    }


    /// <summary>
    /// Starts the process of continually rendering the snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// You take the responsibility of calling this method before you use either <see cref="GetRenderedData(Rect?)"/> or
    /// <see cref="CreateRenderedTexture(Rect?)"/> to get the rendered data or texture. It prepares a render buffer for the image
    /// data and performs the initial rendering for later use.
    /// </para>
    /// <para>
    /// If this method is not called, the related data or texture methods will not work and will only return <c>null</c>. Once you
    /// no longer need the web view to be rendered as a texture, you should call <see cref="StopSnapshotForRendering"/> to clean up
    /// the associated resources.
    /// </para>
    /// </remarks>
    /// <param name="rect">The optional rectangle to specify the area for rendering. If <c>null</c>, the entire view is rendered.</param>
    /// <param name="onStarted">
    /// An optional callback to execute when rendering has started. The callback receives a <see cref="Texture2D"/> parameter
    /// representing the rendered texture.
    /// </param>
    public void StartSnapshotForRendering(Rect? rect = null, Action<Texture2D> onStarted = null) {
        string identifier = null;
        if (onStarted != null) {
            identifier = Guid.NewGuid().ToString();
            actions.Add(identifier, () => {
                var texture = CreateRenderedTexture(rect);
                onStarted(texture);
            });
        }
        UniWebViewInterface.StartSnapshotForRendering(listener.Name, identifier);
    }

    /// <summary>
    /// Stops the process of continually rendering the snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// You should call this method when you no longer need any further data or texture from the
    /// <see cref="GetRenderedData(Rect?)"/> or <see cref="CreateRenderedTexture(Rect?)"/> methods. This helps in releasing
    /// resources and terminating the rendering process.
    /// </para>
    /// </remarks>
    public void StopSnapshotForRendering() {
        UniWebViewInterface.StopSnapshotForRendering(listener.Name);
    }

    /// <summary>
    /// Gets the data of the rendered image for the current web view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method provides you with the raw bytes of the rendered image data in PNG format. To successfully retrieve the
    /// current rendered data, you should first call <see cref="StartSnapshotForRendering"/> to initiate the rendering process.
    /// If <see cref="StartSnapshotForRendering"/> has not been called, this method will return <c>null</c>.
    /// </para>
    /// <para>
    /// The rendering area specified by the <paramref name="rect"/> parameter is based on the local coordinates of the web view.
    /// For example, <c>new Rect(webView.frame.width / 2, webView.frame.height / 2, 100, 100)</c> means setting the origin to the
    /// center of the web view and taking a 100x100 square as the snapshot area.
    /// </para>
    /// <para>
    /// Please note that this method supports only software-rendered content. Content rendered by hardware, such as videos
    /// and WebGL content, will appear as a black rectangle in the rendered image.
    /// </para>
    /// </remarks>
    /// <param name="rect">
    /// The desired rectangle within which the snapshot rendering should occur in the web view. If default value `null` is used,
    /// the whole web view frame will be used as the snapshot area.
    /// </param>
    /// <returns>
    /// An array of raw bytes representing the rendered image data in PNG format, or <c>null</c> if the rendering process fails
    /// or if the data is not prepared.
    /// </returns>
    /// <seealso cref="StartSnapshotForRendering"/>
    public byte[] GetRenderedData(Rect? rect = null) {
        var r = rect ?? snapshotFullViewRect;
        return UniWebViewInterface.GetRenderedData(
            listener.Name, (int)r.x, (int)r.y, (int)r.width, (int)r.height
        );
    }

    /// <summary>
    /// Creates a rendered texture for the current web view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// You should destroy the returned texture using the `Destroy` method when you no longer need it to free up resources.
    /// </para>
    /// <para>
    /// This method provides you with a texture of the rendered image for the web view, which you can use in your 3D game world.
    /// To obtain the current rendered data, you should call <see cref="StartSnapshotForRendering"/> before using this method.
    /// If <see cref="StartSnapshotForRendering"/> has not been called, this method will return <c>null</c>.
    /// </para>
    /// <para>
    /// Please note that this method supports only software-rendered content. Content rendered by hardware, such as videos
    /// and WebGL content, will appear as a black rectangle in the rendered image.
    /// </para>
    /// <para>
    /// This method returns a plain <see cref="Texture2D"/> object. The texture is not user interactive and can only be used for
    /// display purposes. It is your responsibility to call the `Destroy` method on this texture when you no longer need it.
    /// </para>
    /// </remarks>
    /// <param name="rect">
    /// The desired rectangle within which the snapshot rendering should occur in the web view. If default value `null` is used,
    /// the whole web view frame will be used as the snapshot area.
    /// </param>
    /// <returns>
    /// A rendered texture of the current web view, or <c>null</c> if the rendering process fails or if the data is not prepared.
    /// </returns>
    /// <seealso cref="StartSnapshotForRendering"/>
    public Texture2D CreateRenderedTexture(Rect? rect = null) {
        var bytes = GetRenderedData(rect);
        if (bytes == null) {
            return null;
        }
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        texture.LoadImage(bytes);
        return texture;
    }
}