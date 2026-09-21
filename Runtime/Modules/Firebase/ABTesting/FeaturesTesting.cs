using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Runtime.ABTesting
{
    public sealed class FeaturesTesting
    {
        private readonly Dictionary<string, Dictionary<string, ITestableFeature>> _featuresByRemoteId =
            new Dictionary<string, Dictionary<string, ITestableFeature>>();

        public void RunFeature(string remoteId, string groupName)
        {
            TryRunFeature(remoteId, groupName);
        }

        public bool TryRunFeature(string remoteId, string groupName)
        {
            if (!HasFeature(remoteId, groupName)) return false;
            _featuresByRemoteId[remoteId][groupName].Run();
            return true;
        }

        public void AddOrUpdateFeature(string remoteId, string groupName, ITestableFeature feature)
        {
            if (string.IsNullOrWhiteSpace(remoteId))
                throw new ArgumentNullException(nameof(remoteId));

            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentNullException(nameof(groupName));

            if (feature == null)
                throw new ArgumentNullException(nameof(feature));

            if (_featuresByRemoteId.TryGetValue(remoteId, out var featureByGroupName))
            {
                featureByGroupName[groupName] = feature;
                return;
            }

            _featuresByRemoteId[remoteId] = new Dictionary<string, ITestableFeature>
            {
                [groupName] = feature
            };
        }

        public void RemoveFeature(string remoteId, string groupName)
        {
            if (!HasFeature(remoteId, groupName)) return;
            var featureByGroupName = _featuresByRemoteId[remoteId];

            featureByGroupName.Remove(groupName);

            // Автоочистка: если удалили последнюю группу, удаляем и весь remoteId
            if (featureByGroupName.Count == 0)
            {
                _featuresByRemoteId.Remove(remoteId);
            }
        }

        public void RemoveRemoteId(string remoteId)
        {
            if (remoteId != null) _featuresByRemoteId.Remove(remoteId);
        }

        public void Clear()
        {
            _featuresByRemoteId.Clear();
        }

        public string GetFirstGroupForTest(string remoteId)
        {
            if (remoteId != null && _featuresByRemoteId.TryGetValue(remoteId, out var featureByGroupName))
            {
                if (featureByGroupName.Count > 0)
                {
                    foreach (var key in featureByGroupName.Keys)
                    {
                        return key;
                    }
                }
            }
            return null;
        }

        public bool HasFeature(string remoteId, string groupName)
        {
            return remoteId != null && groupName != null
                   && _featuresByRemoteId.TryGetValue(remoteId, out var featureByGroupName)
                   && featureByGroupName.ContainsKey(groupName);
        }

        public bool HasTest(string remoteId)
        {
            return remoteId != null && _featuresByRemoteId.ContainsKey(remoteId);
        }
    }
}
