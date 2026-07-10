#!/usr/bin/env python3
import sys
import time

try:
    import gi

    gi.require_version("Atspi", "2.0")
    from gi.repository import Atspi
except Exception:
    sys.exit(3)

EDITABLE_ROLES = {
    "entry",
    "text",
    "document text",
    "password text",
    "terminal",
    "editable text",
    "combo box",
    "spin button",
    "table cell",
}

SKIP_APPS = {
    "gnome-shell",
    "ibus-extension-gtk3",
    "update-notifier",
    "evolution-alarm-notify",
}


def is_editable_role(role: str) -> bool:
    role = role.lower()
    return role in EDITABLE_ROLES or "text" in role


def is_editable(obj) -> bool:
    try:
        role = obj.get_role_name().lower()
        if not is_editable_role(role):
            return False

        state = obj.get_state_set()
        if state.contains(Atspi.StateType.EDITABLE):
            return Atspi.Text.get_from(obj) is not None

        return Atspi.Text.get_from(obj) is not None
    except Exception:
        return False


def find_focused_editable(obj, depth=0):
    if obj is None or depth > 40:
        return None

    try:
        state = obj.get_state_set()
        if state.contains(Atspi.StateType.FOCUSED) and is_editable(obj):
            return obj
    except Exception:
        pass

    try:
        for i in range(obj.get_child_count()):
            child = obj.get_child_at_index(i)
            found = find_focused_editable(child, depth + 1)
            if found is not None:
                return found
    except Exception:
        pass

    return None


def find_showing_editable(obj, depth=0):
    if obj is None or depth > 40:
        return None

    try:
        state = obj.get_state_set()
        if state.contains(Atspi.StateType.SHOWING) and is_editable(obj):
            return obj
    except Exception:
        pass

    try:
        for i in range(obj.get_child_count()):
            child = obj.get_child_at_index(i)
            found = find_showing_editable(child, depth + 1)
            if found is not None:
                return found
    except Exception:
        pass

    return None


def find_active_descendant_editable(root):
    try:
        active = root.get_active_descendant()
        if active is not None:
            if is_editable(active):
                return active
            found = find_focused_editable(active)
            if found is not None:
                return found
    except Exception:
        pass

    return None


def find_target():
    Atspi.init()

    for desktop_index in range(Atspi.get_desktop_count()):
        desk = Atspi.get_desktop(desktop_index)

        target = find_active_descendant_editable(desk)
        if target is not None:
            return target

        for i in range(desk.get_child_count()):
            app = desk.get_child_at_index(i)
            try:
                app_name = app.get_name().lower()
            except Exception:
                app_name = ""

            if app_name in SKIP_APPS:
                continue

            target = find_focused_editable(app)
            if target is not None:
                return target

        for i in range(desk.get_child_count()):
            app = desk.get_child_at_index(i)
            try:
                app_name = app.get_name().lower()
            except Exception:
                app_name = ""

            if app_name in SKIP_APPS:
                continue

            target = find_showing_editable(app)
            if target is not None:
                return target

    return None


def parse_delay_ms() -> int:
    args = sys.argv[1:]
    for index, arg in enumerate(args):
        if arg == "--delay-ms" and index + 1 < len(args):
            try:
                return max(0, int(args[index + 1]))
            except ValueError:
                return 0
    return 0


def insert_text(text_iface, text: str, delay_ms: int) -> None:
    offset = text_iface.get_caret_offset()
    if delay_ms <= 0:
        text_iface.insert_text(offset, text, len(text))
        return

    for ch in text:
        if ch == "\r":
            continue
        text_iface.insert_text(offset, ch, 1)
        offset += 1
        if delay_ms > 0:
            time.sleep(delay_ms / 1000.0)


def main() -> int:
    delay_ms = parse_delay_ms()
    text = sys.stdin.read()
    if not text:
        return 1

    target = None
    for _ in range(3):
        target = find_target()
        if target is not None:
            break
        time.sleep(0.05)

    if target is None:
        return 2

    text_iface = Atspi.Text.get_from(target)
    insert_text(text_iface, text, delay_ms)
    return 0


if __name__ == "__main__":
    sys.exit(main())
