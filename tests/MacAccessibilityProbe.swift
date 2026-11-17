import AppKit
import ApplicationServices
import Foundation

// This executable belongs to the disposable cloud acceptance session.
func attribute(_ element: AXUIElement, _ name: String) -> CFTypeRef? {
    var value: CFTypeRef?
    return AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success ? value : nil
}

func children(_ element: AXUIElement) -> [AXUIElement] {
    return attribute(element, kAXChildrenAttribute) as? [AXUIElement] ?? []
}

func inputState() -> [String: Any] {
    let application = NSWorkspace.shared.frontmostApplication
    let location = CGEvent(source: nil)?.location ?? .zero
    return ["frontmostPid": application?.processIdentifier ?? -1,
            "frontmostBundle": application?.bundleIdentifier ?? "",
            "mouseX": location.x, "mouseY": location.y]
}

func elementAtPath(_ root: AXUIElement, _ path: String) -> AXUIElement? {
    var current = root
    for component in path.split(separator: "/") {
        guard let index = Int(component) else { return nil }
        let available = children(current)
        guard available.indices.contains(index) else { return nil }
        current = available[index]
    }
    return current
}

func bounds(_ element: AXUIElement) -> CGRect? {
    guard let position = attribute(element, kAXPositionAttribute),
          let size = attribute(element, kAXSizeAttribute),
          CFGetTypeID(position) == AXValueGetTypeID(),
          CFGetTypeID(size) == AXValueGetTypeID() else { return nil }
    var point = CGPoint.zero
    var dimensions = CGSize.zero
    guard AXValueGetValue(position as! AXValue, .cgPoint, &point),
          AXValueGetValue(size as! AXValue, .cgSize, &dimensions),
          dimensions.width > 0, dimensions.height > 0 else { return nil }
    return CGRect(origin: point, size: dimensions)
}

var nodes: [[String: Any]] = []
func inspect(_ element: AXUIElement, _ path: String, _ depth: Int) {
    guard depth <= 32 && nodes.count < 4000 else { return }
    var node: [String: Any] = ["path": path]
    if let rectangle = bounds(element) {
        node["bounds"] = ["x": rectangle.minX, "y": rectangle.minY,
                          "width": rectangle.width, "height": rectangle.height]
    }
    for name in [kAXRoleAttribute, kAXSubroleAttribute, kAXTitleAttribute,
                 kAXDescriptionAttribute, kAXHelpAttribute, kAXIdentifierAttribute,
                 kAXEnabledAttribute, kAXFocusedAttribute, kAXSelectedAttribute,
                 "AXDOMIdentifier", "AXRoleDescription"] {
        if let value = attribute(element, name) {
            if let text = value as? String { node[name] = String(text.prefix(800)) }
            else if let number = value as? NSNumber { node[name] = number }
        }
    }
    if node[kAXSubroleAttribute] as? String != "AXSecureTextField",
       let value = attribute(element, kAXValueAttribute) {
        if let text = value as? String { node[kAXValueAttribute] = String(text.prefix(800)) }
        else if let number = value as? NSNumber { node[kAXValueAttribute] = number }
    }
    var actions: CFArray?
    if AXUIElementCopyActionNames(element, &actions) == .success { node["actions"] = actions as? [String] ?? [] }
    var names: CFArray?
    var writable: [String] = []
    if AXUIElementCopyAttributeNames(element, &names) == .success {
        for name in names as? [String] ?? [] {
            var settable = DarwinBoolean(false)
            if AXUIElementIsAttributeSettable(element, name as CFString, &settable) == .success && settable.boolValue {
                writable.append(name)
            }
        }
    }
    node["writable"] = writable
    nodes.append(node)
    for (index, child) in children(element).enumerated() {
        inspect(child, path.isEmpty ? String(index) : path + "/" + String(index), depth + 1)
    }
}

let args = CommandLine.arguments
guard args.count >= 4, let pid = Int32(args[1]) else {
    fputs("Expected PID, output JSON and operation.\n", stderr)
    exit(2)
}
let root = AXUIElementCreateApplication(pid)
AXUIElementSetMessagingTimeout(root, 3)
var report: [String: Any] = ["trusted": AXIsProcessTrusted(), "targetPid": pid, "before": inputState()]
let operation = args[3]
if operation == "enable" {
    var enhancedSettable = DarwinBoolean(false)
    if AXUIElementIsAttributeSettable(root, "AXEnhancedUserInterface" as CFString, &enhancedSettable) == .success && enhancedSettable.boolValue {
        report["enhancedAccessibilityStatus"] = AXUIElementSetAttributeValue(root, "AXEnhancedUserInterface" as CFString, kCFBooleanTrue).rawValue
    }
    report["manualAccessibilityStatus"] = AXUIElementSetAttributeValue(root, "AXManualAccessibility" as CFString, kCFBooleanTrue).rawValue
    Thread.sleep(forTimeInterval: 2)
} else if operation == "action" && args.count >= 6 {
    if let target = elementAtPath(root, args[4]) {
        report["actionStatus"] = AXUIElementPerformAction(target, args[5] as CFString).rawValue
    } else { report["error"] = "The accessibility path changed before the action." }
    Thread.sleep(forTimeInterval: 1)
} else if operation == "set" && args.count >= 7 {
    if let target = elementAtPath(root, args[4]) {
        let value: CFTypeRef
        if args[6] == "true" { value = kCFBooleanTrue }
        else if args[6] == "false" { value = kCFBooleanFalse }
        else { value = args[6] as CFString }
        report["setStatus"] = AXUIElementSetAttributeValue(target, args[5] as CFString, value).rawValue
    } else { report["error"] = "The accessibility path changed before the edit." }
    Thread.sleep(forTimeInterval: 1)
} else if operation == "drag" && args.count >= 6 {
    if let source = elementAtPath(root, args[4]), let destination = elementAtPath(root, args[5]),
       let startBounds = bounds(source), let endBounds = bounds(destination) {
        let start = CGPoint(x: startBounds.midX, y: startBounds.midY)
        let end = CGPoint(x: endBounds.midX, y: endBounds.minY + 2)
        let down = CGEvent(mouseEventSource: nil, mouseType: .leftMouseDown, mouseCursorPosition: start, mouseButton: .left)
        down?.setIntegerValueField(.mouseEventClickState, value: 1)
        down?.postToPid(pid)
        for step in 1...20 {
            let fraction = Double(step) / 20.0
            let location = CGPoint(x: start.x + (end.x - start.x) * fraction,
                                   y: start.y + (end.y - start.y) * fraction)
            CGEvent(mouseEventSource: nil, mouseType: .leftMouseDragged,
                    mouseCursorPosition: location, mouseButton: .left)?.postToPid(pid)
            Thread.sleep(forTimeInterval: 0.02)
        }
        CGEvent(mouseEventSource: nil, mouseType: .leftMouseUp,
                mouseCursorPosition: end, mouseButton: .left)?.postToPid(pid)
        report["targetedDragSubmitted"] = true
        Thread.sleep(forTimeInterval: 1)
    } else { report["error"] = "The accessibility drag targets have no current bounds." }
} else if operation == "key" && args.count >= 5, let code = UInt16(args[4]) {
    let down = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: true)
    let up = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: false)
    down?.postToPid(pid)
    up?.postToPid(pid)
    report["targetedKeyCode"] = code
    Thread.sleep(forTimeInterval: 1)
}
inspect(root, "", 0)
report["nodes"] = nodes
report["after"] = inputState()
let data = try JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys])
try data.write(to: URL(fileURLWithPath: args[2]), options: .atomic)
print("Accessibility snapshot saved with \(nodes.count) nodes. Trusted \(AXIsProcessTrusted()).")
