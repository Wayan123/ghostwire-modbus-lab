---
inclusion: fileMatch
fileMatchPattern: "frontend/**"
---

# Accessibility

- Use semantic headings and controls, explicit form labels, and visible keyboard focus.
- Show status as text ("Status: timeout", "Unauthorized write"), never by color alone.
- Keep readable contrast and responsive layouts. Respect `prefers-reduced-motion`.
- Frame inspector: show bytes as hex in a monospace font, and give each field (transaction ID, protocol ID, length, unit ID, function, data) a text label. Use a real `<table>` with header cells, or a definition list.
- Traffic logs: use a table with column headers. Mark the suspicious row with text and an icon or pattern as well as color, and let keyboard users reach it.
- Announce new results through the existing `role="status"` regions without moving focus.
- Demo footage should include English captions.
