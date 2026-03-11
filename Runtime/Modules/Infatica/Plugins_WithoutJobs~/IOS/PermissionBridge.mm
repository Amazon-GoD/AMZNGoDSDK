#import <Foundation/Foundation.h>
#import <CoreLocation/CoreLocation.h>

static CLLocationManager* _permissionLocationManager = nil;

@interface PermissionDelegate : NSObject <CLLocationManagerDelegate>
@end

@implementation PermissionDelegate

- (void)locationManager:(CLLocationManager *)manager didChangeAuthorizationStatus:(CLAuthorizationStatus)status {

    // Step 1: WhenInUse granted → request Always (once per app session)
    if (status == kCLAuthorizationStatusAuthorizedWhenInUse) {

        // MUST BE ENABLED BEFORE requesting Always
        if ([manager respondsToSelector:@selector(setAllowsBackgroundLocationUpdates:)]) {
            manager.allowsBackgroundLocationUpdates = YES;
        }

        if ([manager respondsToSelector:@selector(requestAlwaysAuthorization)]) {
            [manager requestAlwaysAuthorization];
        }
    }
}

@end

static PermissionDelegate* _permissionDelegate = nil;

extern "C" void RequestLocationPermission()
{
    if (_permissionLocationManager == nil) {
        _permissionLocationManager = [[CLLocationManager alloc] init];
        _permissionDelegate = [[PermissionDelegate alloc] init];
        _permissionLocationManager.delegate = _permissionDelegate;
    }

    // Must request WhenInUse first
    if ([_permissionLocationManager respondsToSelector:@selector(requestWhenInUseAuthorization)]) {
        [_permissionLocationManager requestWhenInUseAuthorization];
    }
}

extern "C" int GetLocationPermissionStatus()
{
    CLAuthorizationStatus status;

#if __IPHONE_OS_VERSION_MAX_ALLOWED >= 140000
    if (@available(iOS 14.0, *)) {
        status = [CLLocationManager authorizationStatus];
    } else {
        status = [CLLocationManager authorizationStatus];
    }
#else
    status = [CLLocationManager authorizationStatus];
#endif

    return (int)status;
}
