#import <Cocoa/Cocoa.h>
#include <stdatomic.h>
#include <stdbool.h>

static NSStatusItem *status_item;
static NSMenuItem *open_item;
static NSMenuItem *exit_item;
static void (*on_command)(int);
static atomic_bool stopped = false;

@interface SorterTrayActions : NSObject
- (void)openSorter:(id)sender;
- (void)exitSorter:(id)sender;
@end
@implementation SorterTrayActions
- (void)openSorter:(id)sender { on_command(1); }
- (void)exitSorter:(id)sender { on_command(2); }
@end

void sorter_tray_set_captions(const char *title, const char *open, const char *exit) {
    NSString *heading = [NSString stringWithUTF8String:title];
    NSString *openText = [NSString stringWithUTF8String:open];
    NSString *exitText = [NSString stringWithUTF8String:exit];
    dispatch_async(dispatch_get_main_queue(), ^{
        status_item.button.toolTip = heading;
        open_item.title = openText;
        exit_item.title = exitText;
    });
}

void sorter_tray_stop(void) {
    atomic_store(&stopped, true);
    dispatch_async(dispatch_get_main_queue(), ^{
        [NSApp stop:nil];
        NSEvent *wake = [NSEvent otherEventWithType:NSEventTypeApplicationDefined location:NSZeroPoint modifierFlags:0 timestamp:0 windowNumber:0 context:nil subtype:0 data1:0 data2:0];
        [NSApp postEvent:wake atStart:YES];
    });
}

int sorter_tray_run(const char *icon, const char *title, const char *open, const char *exit, void (*command)(int)) {
    @autoreleasepool {
        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
        on_command = command;
        SorterTrayActions *actions = [SorterTrayActions new];
        status_item = [[NSStatusBar systemStatusBar] statusItemWithLength:NSSquareStatusItemLength];
        NSImage *image = [[NSImage alloc] initWithContentsOfFile:[NSString stringWithUTF8String:icon]];
        image.size = NSMakeSize(18, 18);
        status_item.button.image = image;
        status_item.button.toolTip = [NSString stringWithUTF8String:title];
        NSMenu *menu = [NSMenu new];
        open_item = [[NSMenuItem alloc] initWithTitle:[NSString stringWithUTF8String:open] action:@selector(openSorter:) keyEquivalent:@""];
        exit_item = [[NSMenuItem alloc] initWithTitle:[NSString stringWithUTF8String:exit] action:@selector(exitSorter:) keyEquivalent:@""];
        open_item.target = actions;
        exit_item.target = actions;
        [menu addItem:open_item];
        [menu addItem:exit_item];
        status_item.menu = menu;
        if (!atomic_load(&stopped)) [NSApp run];
        [[NSStatusBar systemStatusBar] removeStatusItem:status_item];
        status_item = nil;
        return 0;
    }
}
