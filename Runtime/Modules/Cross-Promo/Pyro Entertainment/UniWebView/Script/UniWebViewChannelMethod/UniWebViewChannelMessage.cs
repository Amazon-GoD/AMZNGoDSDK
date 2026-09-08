//
//  UniWebViewChannelMessage.cs
//  Created by Wang Wei(@onevcat) on 2025-08-02.
//
//  This file is a part of UniWebView Project (https://uniwebview.com)
//  By purchasing the asset, you are allowed to use this code in as many as projects 
//  you want, only if you publish the final products under the name of the same account
//  used for the purchase. 
//
//  This asset and all corresponding files (such as source code) are provided on an 
//  "as is" basis, without warranty of any kind, express of implied, including but not
//  limited to the warranties of merchantability, fitness for a particular purpose, and 
//  noninfringement. In no event shall the authors or copyright holders be liable for any 
//  claim, damages or other liability, whether in action of contract, tort or otherwise, 
//  arising from, out of or in connection with the software or the use of other dealing in the software.
//

using System;
using UniWebViewExternal;

/// <summary>
/// Channel message types corresponding to different JavaScript call methods
/// </summary>
public enum UniWebViewChannelMessageType {
    /// <summary>
    /// Fire-and-forget message (window.uniwebview.send)
    /// </summary>
    Send = 0,

    /// <summary>
    /// Synchronous call with immediate response (window.uniwebview.call)
    /// </summary>
    Call = 1,

    /// <summary>
    /// Asynchronous request with promise-based response (window.uniwebview.request)
    /// </summary>
    Request = 2
}

/// <summary>
/// Response delegate for async messages
/// </summary>
/// <param name="messageId">The message ID to respond to</param>
/// <param name="responseJson">The response Json string to send back</param>
public delegate void ChannelMessageRespondDelegate(string messageId, UniWebViewChannelMessageResponse response);

/// <summary>
/// Represents a channel message received from the web view's JavaScript Bridge
/// </summary>
[Serializable]
public class UniWebViewChannelMessage {
    /// <summary>
    /// Unique identifier for this message
    /// </summary>
    public string id;
    
    /// <summary>
    /// Timestamp when the message was created (Unix milliseconds)
    /// </summary>
    public long timestamp;
    
    /// <summary>
    /// The action type for this message (required)
    /// </summary>
    public string action;
    
    /// <summary>
    /// The message data as a JSON string (optional)
    /// </summary>
    public string data;

    /// <summary>
    /// The type of this channel message (send/call/request)
    /// </summary>
    public int messageType;
    
    // Non-serialized delegate for async responses
    [System.NonSerialized]
    private ChannelMessageRespondDelegate _respondDelegate;
    
    // Internal constructor with respond delegate
    internal UniWebViewChannelMessage(string id, long timestamp, string action, string data, int messageType, ChannelMessageRespondDelegate respondDelegate) {
        this.id = id;
        this.timestamp = timestamp;
        this.action = action;
        this.data = data;
        this.messageType = messageType;
        this._respondDelegate = respondDelegate;
    }
    
    // Default constructor for JSON deserialization
    public UniWebViewChannelMessage() { }

    /// <summary>
    /// Gets the strongly-typed message type
    /// </summary>
    public UniWebViewChannelMessageType MessageType => (UniWebViewChannelMessageType)messageType;

    /// <summary>
    /// True if this is a fire-and-forget message (send)
    /// </summary>
    public bool isFireAndForget => MessageType == UniWebViewChannelMessageType.Send;

    /// <summary>
    /// True if this is a synchronous call (call)
    /// </summary>
    public bool isSyncCall => MessageType == UniWebViewChannelMessageType.Call;

    /// <summary>
    /// True if this is an asynchronous request (request)
    /// </summary>
    public bool isAsyncRequest => MessageType == UniWebViewChannelMessageType.Request;

    public bool TryGetData<T>(out T result) {
        result = default;

        try {
            result = UniWebViewJsonUtility.FromJson<T>(data);
            return true;
        } catch (Exception e) {
            UniWebViewLogger.Instance.Critical(() => $"Failed to parse message data for action '{action}': {e.Message}");
            return false;
        }
    }
    
    public T GetData<T>() {
        if (TryGetData<T>(out var result)) {
            return result;
        }
        return default(T);
    }
    
    // ReSharper disable Unity.PerformanceAnalysis
    /// <summary>
    /// Sends an async response for this message
    /// </summary>
    /// <param name="response">The response to send back</param>
    public void Respond(UniWebViewChannelMessageResponse response) {
        if (!isAsyncRequest) {
            UniWebViewLogger.Instance.Critical(() => $"Cannot respond to a {MessageType} message. Only Request messages support async responses.");
            return;
        }

        if (_respondDelegate == null) {
            UniWebViewLogger.Instance.Critical(() => "Cannot respond: Response delegate is null (message may have been deserialized or webview destroyed)");
            return;
        }
        _respondDelegate(id, response);
    }
    
    /// <summary>
    /// Convenience method for success responses
    /// </summary>
    /// <param name="data">The data to send back</param>
    public void Respond(object data) {
        Respond(UniWebViewChannelMessageResponse.Success(data));
    }
    
    /// <summary>
    /// Convenience method for error responses
    /// </summary>
    /// <param name="errorData">The error data to send back (supports any JSON-compatible type)</param>
    public void RespondError(object errorData) {
        Respond(UniWebViewChannelMessageResponse.Error(errorData));
    }
}

/// <summary>
/// Represents a response for a channel message
/// </summary>
public class UniWebViewChannelMessageResponse {
    
    [Serializable]
    class ResultWrapper {
        public string result;
    }
    
    [Serializable]
    class ErrorWrapper {
        public string error;
    }
    
    /// <summary>
    /// Response data
    /// </summary>
    public object data;
    
    /// <summary>
    /// Whether this response represents an error
    /// </summary>
    public bool hasError;
    
    /// <summary>
    /// Error data if hasError is true (supports any JSON-compatible type)
    /// </summary>
    public object errorData;
    
    /// <summary>
    /// Creates a success response
    /// </summary>
    /// <param name="data">The data to include in the response</param>
    public UniWebViewChannelMessageResponse(object data) {
        this.data = data;
        this.hasError = false;
        this.errorData = null;
    }
    
    /// <summary>
    /// Creates an error response
    /// </summary>
    /// <param name="errorData">The error data (supports any JSON-compatible type)</param>
    private UniWebViewChannelMessageResponse(object errorData, bool isError) {
        this.data = null;
        this.hasError = true;
        this.errorData = errorData;
    }

    internal string ToJson() {
        if (hasError) {
            return UniWebViewJsonUtility.ToJson(new ErrorWrapper { error = UniWebViewJsonUtility.ToJson(errorData) });
        } else {
            return UniWebViewJsonUtility.ToJson(new ResultWrapper { result = UniWebViewJsonUtility.ToJson(data) });
        }
    }
    
    /// <summary>
    /// Static factory method for success responses
    /// </summary>
    /// <param name="data">The data to include</param>
    /// <returns>A success response</returns>
    public static UniWebViewChannelMessageResponse Success(object data) {
        return new UniWebViewChannelMessageResponse(data);
    }
    
    /// <summary>
    /// Static factory method for error responses
    /// </summary>
    /// <param name="errorData">The error data (supports any JSON-compatible type)</param>
    /// <returns>An error response</returns>
    public static UniWebViewChannelMessageResponse Error(object errorData) {
        return new UniWebViewChannelMessageResponse(errorData, true);
    }
}