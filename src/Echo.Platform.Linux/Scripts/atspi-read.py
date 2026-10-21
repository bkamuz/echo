#!/usr/bin/env python3
"""Prints the focused editable AT-SPI2 text field as one JSON line: {id, pid, app, password, text}.
Exit 0 with output on success, 1 when no focused field, 3 when AT-SPI is unavailable."""
import json
import sys

try:
    import gi

    gi.require_version("Atspi", "2.0")
    from gi.repository import Atspi
except Exception:
    sys.exit(3)

MAX_CHARS = 20000


def focused(obj, depth=0):
    if obj is None or depth > 40:
        return None
    try:
        st = obj.get_state_set()
        if st.contains(Atspi.StateType.FOCUSED) and obj.get_text_iface() is not None:
            return obj
        # Skip hidden subtrees (inactive windows/apps) to keep the walk cheap.
        if depth >= 2 and not st.contains(Atspi.StateType.SHOWING):
            return None
    except Exception:
        pass
    try:
        for i in range(obj.get_child_count()):
            found = focused(obj.get_child_at_index(i), depth + 1)
            if found is not None:
                return found
    except Exception:
        pass
    return None


def find():
    desk = Atspi.get_desktop(0)
    apps = []
    for i in range(desk.get_child_count()):
        app = desk.get_child_at_index(i)
        if app is None:
            continue
        try:
            active = any(
                app.get_child_at_index(j).get_state_set().contains(Atspi.StateType.ACTIVE)
                for j in range(app.get_child_count()))
        except Exception:
            active = False
        apps.append((0 if active else 1, app))
    for _, app in sorted(apps, key=lambda a: a[0]):
        found = focused(app)
        if found is not None:
            return found
    return None


def main():
    Atspi.init()
    obj = find()
    if obj is None:
        sys.exit(1)
    count = Atspi.Text.get_character_count(obj)
    value = Atspi.Text.get_text(obj, 0, count) if count <= MAX_CHARS else None
    pid = obj.get_process_id()
    path = getattr(obj, "path", "") or ""
    ident = "%s|%s|%s" % (pid, path, obj.get_role_name())
    app = ""
    try:
        with open("/proc/%d/comm" % pid) as f:
            app = f.read().strip()
    except Exception:
        try:
            app = obj.get_application().get_name()
        except Exception:
            app = "?"
    role = obj.get_role_name().lower()
    print(json.dumps({"id": ident, "pid": pid, "app": app, "password": role == "password text", "text": value}))


if __name__ == "__main__":
    main()
