#import <UnityInfaticaFramework/BackgroundMode.h>

#ifdef __cplusplus
extern "C" {
#endif

void BeginBackgroundTask() {
    [BackgroundMode beginBackgroundTaskIfNeeded];
}

void EndBackgroundTask() {
    [BackgroundMode endBackgroundTaskIfNeeded];
}

void StartLocation() {
    [BackgroundMode startLocationPermission];
}

const char* GetAgentId() {
    NSString* agentId = [BackgroundMode getAgentId];
    return agentId != nil ? strdup([agentId UTF8String]) : strdup("");
}

void StartAgent(const char* partnerId) {
    NSString* partner = partnerId ? [NSString stringWithUTF8String:partnerId] : @"";
    [BackgroundMode startAgent:partner];
}

void StopAgent(int timeout) {
    [BackgroundMode stopAgent:timeout];
}

#ifdef __cplusplus
}
#endif
