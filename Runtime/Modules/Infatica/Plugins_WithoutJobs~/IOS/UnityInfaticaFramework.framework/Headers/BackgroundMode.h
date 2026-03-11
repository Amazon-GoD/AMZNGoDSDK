//
//  BackgroundMode.h
//  UnityInfaticaFramework
//
//  Created by Denis Senichkin on 08.12.2025.
//

#import <Foundation/Foundation.h>

@interface BackgroundMode : NSObject

+ (void)stopAgent:(NSInteger)timeout;
+ (void)beginBackgroundTaskIfNeeded;
+ (void)endBackgroundTaskIfNeeded;
+ (NSString*)getAgentId;
+ (void)startLocationPermission;
//universal
+ (void)startAgent:(NSString*)partnerId;

@end
