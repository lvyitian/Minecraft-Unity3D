﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Used for authenticating with mojang's servers.
/// All methods are blocking
/// </summary>
public static class MojangAuthentication
{
	public static string Username { get; private set; }

	private const string RequestServer = @"https://authserver.mojang.com";
	private const string clientTokenPrefKey = "authClientToken";
	private const string accessTokenPrefKey = "authAccessToken";

	/// <summary>
	/// The access token to be used by the client
	/// </summary>
	public static string AccessToken
	{
		get
		{
			return PlayerPrefs.GetString(accessTokenPrefKey, null);
		}
		set
		{
			PlayerPrefs.SetString(accessTokenPrefKey, value);
			PlayerPrefs.Save();
		}
	}

	/// <summary>
	/// Retreives the semi-permanent client token for this client
	/// </summary>
	/// <returns></returns>
	public static string GetClientToken()
	{
		// generate new client token if we need
		if (!PlayerPrefs.HasKey(clientTokenPrefKey))
		{
			string newToken = Guid.NewGuid().ToString();
			PlayerPrefs.SetString(clientTokenPrefKey, newToken);
			PlayerPrefs.Save();
			return newToken;
		}

		return PlayerPrefs.GetString(clientTokenPrefKey);
	}

	/// <summary>
	/// Logs client into a 
	/// </summary>
	/// <param name="username"></param>
	/// <param name="password"></param>
	/// <returns></returns>
	public static IEnumerator Login(string username, string password, Action<AccountStatus> callback)
	{
		// send login request to server
		var responseData = new ResponseData();
		MakeRequest<RefreshResponse>("authenticate", JsonUtility.ToJson(new AuthenticatePayload(username, password)), responseData);

		while (!responseData.IsCompleted)
			yield return null;

		callback(HandleRefreshResponse(responseData));
	}

	/// <summary>
	/// Gets the stored login status of the user
	/// </summary>
	/// <returns></returns>
	public static IEnumerator GetLoginStatus(Action<AccountStatus> callback)
	{
		if (AccessToken == null)
		{
			callback(AccountStatus.LOGGED_OUT);
		}

		// refresh access token
		var responseData = new ResponseData();
		MakeRequest<RefreshResponse>("refresh", JsonUtility.ToJson(new RefreshPayload(AccessToken)), responseData);

		// wait for web request to complete
		while (!responseData.IsCompleted)
			yield return null;

		callback(HandleRefreshResponse(responseData));
	}

	/// <summary>
	/// Invalidates a specified access token, or the stored one if no token is specified
	/// </summary>
	/// <param name="token"></param>
	public static IEnumerator InvalidateAccessToken(string token = null)
	{
		var responseData = new ResponseData(); 
		MakeRequest<object>("invalidate", JsonUtility.ToJson(new InvalidatePayload(token ?? AccessToken)), responseData);

		while (!responseData.IsCompleted)
			yield return null;
	}

	/// <summary>
	/// Checks a refresh response and tells us the status of it
	/// </summary>
	/// <param name="responseData"></param>
	/// <returns></returns>
	private static AccountStatus HandleRefreshResponse(ResponseData responseData)
	{
		// check if response is errored
		if (responseData.ErrorData != null)
		{
			// check if token invalid
			if (responseData.ErrorData.Error == "ForbiddenOperationException")
			{
				AccessToken = null; // clear access token because the one we have is invalid
				return AccountStatus.INVALID_CREDENTIALS;
			}

			// misc server error
			throw new Exception($"Error validating token: {responseData.ErrorData}");
		}

		// get body from response data
		var responseBody = (RefreshResponse)responseData.ResponseObject;
		Username = responseBody.SelectedProfile.Name;
		AccountStatus status;

		// check if user is premium
		if (responseBody.SelectedProfile == null)
		{
			status = AccountStatus.NOT_PREMIUM;
		}
		else
		{
			status = AccountStatus.LOGGED_IN;
		}

		// store client token if login valid
		if (status == AccountStatus.LOGGED_IN)
		{
			AccessToken = responseBody.AccessToken;
		}

		return status;
	}

	/// <summary>
	/// Posts the data and returns the response.
	/// </summary>
	/// <param name="endpoint">The endpoint to post to</param>
	/// <param name="jsonData"></param>
	/// <typeparam name="T">The type to deserialze the response into</typeparam>
	/// <returns></returns>
	private static IEnumerator MakeRequest<T>(string endpoint, string jsonData, ResponseData response)
	{
		var request = new UnityWebRequest($"{RequestServer}/{endpoint}", "POST")
		{
			timeout = 30
		};

		request.SetRequestHeader("content-type", "application/json");
		request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonData));
		request.downloadHandler = new DownloadHandlerBuffer();
		request.SendWebRequest();
		while (!request.isDone)
			yield return null;

		// check if request failed
		if (request.responseCode != 200)
		{
			// try to parse server error json
			ServerErrorResponse error;
			try
			{
				error = JsonUtility.FromJson<ServerErrorResponse>(request.downloadHandler.text);
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"Exception deserializing auth server error:\n{ex}\nBody:\n{request.downloadHandler.text}");
				error = new ServerErrorResponse(ex);
			}

			response.ErrorData = error;
		}
		else
		{
			// attempt to parse response
			try
			{
				T responseObject = JsonUtility.FromJson<T>(request.downloadHandler.text);
				response.ResponseObject = responseObject;
			}
			catch (Exception ex)
			{
				// error parsing response
				Debug.LogWarning($"Exception deserializing server response:\n{ex}\nBody:\n{request.downloadHandler.text}");
				response.ErrorData = new ServerErrorResponse(ex);
			}
		}

		// mark the response as completed
		response.IsCompleted = true;
	}

	/// <summary>
	/// Determines a user's login status
	/// </summary>
	public enum AccountStatus
	{
		LOGGED_IN,
		LOGGED_OUT,
		NOT_PREMIUM,
		INVALID_CREDENTIALS
	}
}

class ResponseData
{
	public ResponseData()
	{ }

	public ResponseData(object boxedResponse)
	{
		ResponseObject = boxedResponse;
	}

	/// <summary>
	/// Whether this response has all it's data. For use with <see cref="MojangAuthentication.MakeRequest{T}(string, string, ResponseData)"/>
	/// </summary>
	public bool IsCompleted { get; set; }

	/// <summary>
	/// The error this response contains
	/// </summary>
	public ServerErrorResponse ErrorData { get; set; } = null;

	/// <summary>
	/// A boxed version of the response object
	/// </summary>
	public object ResponseObject { get; set; }
}