"""edit_tiles.renderer ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â DrawMixin: draw sub-methods for TileEditor.

TileEditor.draw() calls these methods. Each covers one visual layer.
"""
import math
import pygame
from .constants import (
    BG_COLOR, BORDER_COLOR, MISSING_COLOR, WARN_COLOR, DIM_COLOR,
    ACCENT_COLOR, ACCENT2_COLOR, OK_COLOR, TEXT_COLOR, TEXT_SOFT, PANEL_COLOR, TOOLBAR_COLOR,
    PANEL_SECTION_BG, PANEL_SECTION_BORDER,
    PANEL_H, TOOLBAR_H, STATUS_H, TILE_STRIDE,
    BRUSH_COLORS, BTN_BORDER, BTN_HOVER_C, BTN_INACTIVE,
    VEG_COLORS, VEG_NAMES, VEG_DESCRIPTIONS,
    _MOD_AVAILABLE, _BRIDGE_AVAILABLE,
)


MODE_COLORS = {'height': ACCENT_COLOR, 'veg': OK_COLOR, 'water': (80, 140, 255)}
SHELL_SIDEBAR_W = 200
SHELL_GAP = 12
FLOWY_TANGENT_FACTOR = 0.41


def _flowy_forward(rot_y_deg):
    angle = math.radians(float(rot_y_deg))
    return math.sin(angle), math.cos(angle)


def _flowy_segment_points(pt0, pt1):
    pos0 = pt0.get('position', {}) or {}
    pos1 = pt1.get('position', {}) or {}
    x0 = float(pos0.get('x', 0.0))
    z0 = float(pos0.get('z', 0.0))
    x1 = float(pos1.get('x', 0.0))
    z1 = float(pos1.get('z', 0.0))
    dx = x1 - x0
    dz = z1 - z0
    dist = math.hypot(dx, dz)
    if dist < 0.001:
        return [(x0, z0), (x1, z1)]

    rot0 = float((pt0.get('rotation', {}) or {}).get('y', 0.0))
    rot1 = float((pt1.get('rotation', {}) or {}).get('y', 0.0))
    fx0, fz0 = _flowy_forward(rot0)
    fx1, fz1 = _flowy_forward(rot1)
    tangent_len = dist * FLOWY_TANGENT_FACTOR
    c1x = x0 + fx0 * tangent_len
    c1z = z0 + fz0 * tangent_len
    c2x = x1 - fx1 * tangent_len
    c2z = z1 - fz1 * tangent_len
    steps = max(8, min(48, int(dist / 20.0)))
    samples = []
    for idx in range(steps + 1):
        t = idx / max(1, steps)
        omt = 1.0 - t
        x = (
            (omt ** 3) * x0 +
            3.0 * (omt ** 2) * t * c1x +
            3.0 * omt * (t ** 2) * c2x +
            (t ** 3) * x1
        )
        z = (
            (omt ** 3) * z0 +
            3.0 * (omt ** 2) * t * c1z +
            3.0 * omt * (t ** 2) * c2z +
            (t ** 3) * z1
        )
        samples.append((x, z))
    return samples


def _draw_flow_arrow(surface, polyline, color):
    if len(polyline) < 2:
        return
    lengths = []
    total = 0.0
    for idx in range(len(polyline) - 1):
        x0, y0 = polyline[idx]
        x1, y1 = polyline[idx + 1]
        seg_len = math.hypot(x1 - x0, y1 - y0)
        lengths.append(seg_len)
        total += seg_len
    if total < 18.0:
        return

    target = total * 0.55
    walked = 0.0
    for idx, seg_len in enumerate(lengths):
        if seg_len < 0.001:
            continue
        if walked + seg_len < target:
            walked += seg_len
            continue
        x0, y0 = polyline[idx]
        x1, y1 = polyline[idx + 1]
        t = (target - walked) / seg_len
        cx = x0 + (x1 - x0) * t
        cy = y0 + (y1 - y0) * t
        ux = (x1 - x0) / seg_len
        uy = (y1 - y0) / seg_len
        nx = -uy
        ny = ux
        tip = (cx + ux * 8.0, cy + uy * 8.0)
        back = (cx - ux * 6.0, cy - uy * 6.0)
        left = (back[0] + nx * 4.0, back[1] + ny * 4.0)
        right = (back[0] - nx * 4.0, back[1] - ny * 4.0)
        outline = [(int(round(px)), int(round(py))) for px, py in (tip, left, right)]
        pygame.draw.polygon(surface, (0, 0, 0), outline)
        inset = [(int(round(px)), int(round(py))) for px, py in (
            (cx + ux * 6.0, cy + uy * 6.0),
            (back[0] + nx * 3.0, back[1] + ny * 3.0),
            (back[0] - nx * 3.0, back[1] - ny * 3.0),
        )]
        pygame.draw.polygon(surface, color, inset)
        return


def _segment_style_accent(style):
    style_name = str(style or 'Standard')
    if style_name == 'Bridge':
        return (180, 120, 72)
    if style_name == 'Tunnel':
        return (150, 100, 210)
    if style_name == 'Yard':
        return (205, 205, 110)
    return None


def _draw_segment_style_overlay(surface, screen_pts, style, zoom, font=None):
    accent = _segment_style_accent(style)
    if accent is None or len(screen_pts) < 2:
        return
    width = 1 if style == 'Yard' else 2
    for idx in range(len(screen_pts) - 1):
        if style == 'Tunnel' and idx % 2 == 1:
            continue
        pygame.draw.line(surface, accent, screen_pts[idx], screen_pts[idx + 1], width)
    if font and zoom > 0.28:
        mid = screen_pts[len(screen_pts) // 2]
        font.render_to(surface, (mid[0] + 4, mid[1] - 10), str(style), accent)


class DrawMixin:
    """Draw sub-methods extracted from TileEditor.draw()."""

    def _shell_active_section(self):
        if self.mod_panel:
            return 'project'
        if self.area_panel:
            return 'towns'
        if self.prog_panel:
            return 'progression'
        if (self.geo_panel or self.span_panel or self.scenery_panel or self.group_panel
                or self.spliney_panel
                or self.calc_panel or self.mandela_panel or self.gen_panel):
            return 'tools'
        return 'canvas'

    def _draw_shell_sidebar(self, w, h, content_top, mx0, my0):
        self._shell_sidebar_rects = []
        side_h = h - content_top - STATUS_H - 16
        side_x = 12
        side_y = content_top + 8
        side_rect = pygame.Rect(side_x, side_y, SHELL_SIDEBAR_W, side_h)
        self._shell_sidebar_bounds = side_rect

        panel = pygame.Surface((side_rect.width, side_rect.height), pygame.SRCALPHA)
        panel.fill((10, 15, 24, 224))
        self.screen.blit(panel, side_rect.topleft)
        pygame.draw.rect(self.screen, (32, 46, 66), side_rect, 1, border_radius=10)
        pygame.draw.line(
            self.screen,
            (0, 212, 255),
            (side_rect.x, side_rect.y + 38),
            (side_rect.right, side_rect.y + 38),
            1,
        )

        def draw_section(title, y, body_h):
            rect = pygame.Rect(side_rect.x + 8, y, side_rect.width - 16, body_h)
            pygame.draw.rect(self.screen, (14, 20, 30), rect, border_radius=7)
            pygame.draw.rect(self.screen, (36, 52, 72), rect, 1, border_radius=7)
            self.font_big.render_to(self.screen, (rect.x + 10, rect.y + 7), title, ACCENT_COLOR)
            pygame.draw.line(self.screen, (36, 52, 72),
                             (rect.x + 6, rect.y + 24), (rect.right - 6, rect.y + 24), 1)
            return rect

        def draw_small_text(x, y, text, color=DIM_COLOR):
            self.font.render_to(self.screen, (x, y), text, color)

        def draw_grid_button(rect, label, action, active=False, color=ACCENT_COLOR, enabled=True):
            hover = rect.collidepoint(mx0, my0)
            if not enabled:
                fill = (24, 28, 34)
                border = (56, 64, 76)
                text_col = (90, 98, 110)
            elif active:
                fill = color
                border = color
                text_col = TEXT_COLOR
            elif hover:
                fill = tuple(max(18, c // 2) for c in color)
                border = color
                text_col = TEXT_COLOR
            else:
                fill = BTN_INACTIVE
                border = tuple(max(40, c // 3) for c in color)
                text_col = TEXT_COLOR
            pygame.draw.rect(self.screen, fill, rect, border_radius=5)
            pygame.draw.rect(self.screen, border, rect, 1, border_radius=5)
            if active:
                pygame.draw.rect(self.screen, color, (rect.x, rect.bottom - 3, rect.width, 3), border_radius=2)
            label_col = color if (active and enabled) else text_col
            tr, _ = self.font.render(label, label_col)
            self.screen.blit(
                tr,
                (rect.x + (rect.width - tr.get_width()) // 2,
                 rect.y + (rect.height - tr.get_height()) // 2),
            )
            if enabled:
                self._shell_sidebar_rects.append((rect, action))

        y = side_rect.y + 12
        self.font_big.render_to(self.screen, (side_rect.x + 14, y), "Workspace", ACCENT_COLOR)
        y += 18
        project_name = self.mod_project.name if self.mod_project else "No project loaded"
        draw_small_text(side_rect.x + 14, y, project_name, TEXT_COLOR)
        y += 14
        if self.mod_project:
            dirty = "dirty" if self.mod_project.dirty else "clean"
            draw_small_text(
                side_rect.x + 14,
                y,
                f"{len(self.mod_project.layers)} layers  |  {len(self.mod_project.merged_nodes)} nodes  |  {dirty}",
            )
        else:
            draw_small_text(side_rect.x + 14, y, "Load a mod or tiles to get started.")
        y += 18
        draw_small_text(side_rect.x + 14, y, f"UI scale {self._ui_scale_label()}", TEXT_SOFT)
        ui_y = y - 4
        ui_steps = list(getattr(self, 'ui_scale_steps', [1.0])) or [1.0]
        ui_scale = float(getattr(self, 'ui_scale', 1.0))
        ui_specs = [
            ("A-", "ui_scale_down", False, ACCENT_COLOR, ui_scale > min(ui_steps) + 1e-6),
            (self._ui_scale_label(), "ui_scale_reset", abs(ui_scale - 1.0) < 1e-6, ACCENT2_COLOR, True),
            ("A+", "ui_scale_up", False, ACCENT_COLOR, ui_scale < max(ui_steps) - 1e-6),
        ]
        ui_x = side_rect.right - 10
        ui_rects = []
        for label, action, active, color, enabled in reversed(ui_specs):
            bw = max(26, self.font.get_rect(label).width + 14)
            ui_x -= bw
            ui_rects.append((pygame.Rect(ui_x, ui_y, bw, 22), label, action, active, color, enabled))
            ui_x -= 6
        for rect, label, action, active, color, enabled in reversed(ui_rects):
            draw_grid_button(rect, label, action, active=active, color=color, enabled=enabled)
        y += 28

        apply_live = bool(getattr(self, 'live_mod_apply', True))
        pending_apply = int(self._pending_mod_apply_count()) if hasattr(self, '_pending_mod_apply_count') else 0
        apply_text = "Auto Save edits" if apply_live else f"Manual Save edits  |  {pending_apply} pending"
        draw_small_text(side_rect.x + 14, y, apply_text, TEXT_SOFT if apply_live else WARN_COLOR)
        apply_y = y - 4
        apply_specs = [
            ("Auto Save", "apply_mode_live", apply_live, OK_COLOR, True),
            ("Manual", "apply_mode_batch", not apply_live, WARN_COLOR, True),
        ]
        apply_x = side_rect.right - 10
        apply_rects = []
        for label, action, active, color, enabled in reversed(apply_specs):
            bw = max(34, self.font.get_rect(label).width + 14)
            apply_x -= bw
            apply_rects.append((pygame.Rect(apply_x, apply_y, bw, 22), label, action, active, color, enabled))
            apply_x -= 6
        for rect, label, action, active, color, enabled in reversed(apply_rects):
            draw_grid_button(rect, label, action, active=active, color=color, enabled=enabled)
        y += 28

        # Layout constants
        # btn_h=24, gap=6 ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ row_stride=30
        # card body_h = 38 (title+sep+pad) + rows*30 + 6 (bottom pad)
        # Project 2 rows=104, Editors 4 rows=164, View 4 rows=164, Edit 2 rows=104
        # Total ~586px ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â fits at 768px with 68px spare (minimap renders fine).
        pad = 8
        btn_w = (SHELL_SIDEBAR_W - pad * 2 - 6) // 2
        btn_h = 24
        row_stride = 30   # btn_h(24) + gap(6)
        btn_y_offset = 30  # from card.y to first button row (below title+sep)

        card = draw_section("Project", y, 104)
        buttons = [
            ("Open Mod",   "open_mod",       bool(self.mod_project),       (0, 160, 220)),
            ("Load Tiles", "load_tiles",      bool(self.tiles),             (80, 180, 255)),
            ("Load Graph", "load_graph",      bool(self.track_segments),    (120, 160, 255)),
            ("Reload",     "reload_sources",  self._has_reloadable_source(),
             WARN_COLOR if self._has_unsaved_reload_changes() else ACCENT_COLOR),
        ]
        by = card.y + btn_y_offset
        for idx, (label, action, active, color) in enumerate(buttons):
            col = idx % 2
            row = idx // 2
            rect = pygame.Rect(card.x + pad + col * (btn_w + 6), by + row * row_stride, btn_w, btn_h)
            draw_grid_button(rect, label, action, active=active, color=color, enabled=True)
        y = card.bottom + 6

        card = draw_section("Editors", y, 164)
        editor_buttons = [
            ("Project",  "toggle_mod",       self.mod_panel,  (160, 100, 255)),
            ("Towns",    "open_areas",        self.area_panel, (100, 200, 140)),
            ("Progress", "open_progression",  self.prog_panel, (255, 140, 0)),
            ("Generate", "toggle_generate",   self.gen_panel,  OK_COLOR),
            ("Spans",    "toggle_spans",      self.span_panel, (100, 180, 255)),
            ("Spliney",  "toggle_spliney",    getattr(self, 'spliney_panel', False), (0, 170, 200)),
            ("Geo",      "toggle_geo",        self.geo_panel,  (255, 80, 160)),
        ]
        by = card.y + btn_y_offset
        for idx, (label, action, active, color) in enumerate(editor_buttons):
            col = idx % 2
            row = idx // 2
            rect = pygame.Rect(card.x + pad + col * (btn_w + 6), by + row * row_stride, btn_w, btn_h)
            draw_grid_button(rect, label, action, active=active, color=color, enabled=True)
        y = card.bottom + 6

        card = draw_section("View", y, 194)
        view_buttons = [
            ("Heightmap", "mode_height",      self.mode == 'height',    MODE_COLORS['height']),
            ("Vegetation","mode_veg",          self.mode == 'veg',       MODE_COLORS['veg']),
            ("Water",     "mode_water",        self.mode == 'water',     MODE_COLORS['water']),
            ("Hillshade", "toggle_hillshade",  self.hillshade,           ACCENT_COLOR),
            ("Tracks",    "toggle_tracks",     self.show_tracks,         (255, 230, 0)),
            ("Nodes",     "toggle_nodes",      self.show_nodes,          (255, 230, 0)),
            ("Elev Color","toggle_elev_colors",self.show_elev_colors,    (80, 200, 255)),
            ("Grades",    "toggle_grade_labels",self.show_grade_labels,  (0, 220, 140)),
            ("OTM",       "toggle_osm",        self.osm.enabled,         (100, 200, 120)),
            ("Scenery",   "toggle_scenery",    self.scenery_panel,       (200, 130, 255)),
        ]
        by = card.y + btn_y_offset
        for idx, (label, action, active, color) in enumerate(view_buttons):
            col = idx % 2
            row = idx // 2
            enabled = label not in ("Nodes", "Elev Color", "Grades") or self.show_tracks
            rect = pygame.Rect(card.x + pad + col * (btn_w + 6), by + row * row_stride, btn_w, btn_h)
            draw_grid_button(rect, label, action, active=active, color=color, enabled=enabled)
        y = card.bottom + 6

        has_unsaved_workspace_edits = (
            any(t.dirty for t in list(self.tiles.values())) or
            bool(self.mod_project and (
                self.mod_project.dirty or
                self._area_dirty_layers or
                self._pending_bridge_reload_paths
            )) or
            bool(self.prog_project and self.prog_project.dirty)
        )
        card = draw_section("Edit", y, 164)
        edit_buttons = [
            ("Edit Mode", "toggle_edit", self.edit_mode,                                  ACCENT2_COLOR),
            ("Save",      "save_all",    has_unsaved_workspace_edits,                     WARN_COLOR),
            ("Undo",      "undo",        bool(self.undo_stack),                            ACCENT_COLOR),
            ("Help",      "toggle_help", self.show_help,                                   DIM_COLOR),
        ]
        by = card.y + btn_y_offset
        for idx, (label, action, active, color) in enumerate(edit_buttons):
            col = idx % 2
            row = idx // 2
            rect = pygame.Rect(card.x + pad + col * (btn_w + 6), by + row * row_stride, btn_w, btn_h)
            enabled = action != "undo" or bool(self.undo_stack)
            if action == "save_all":
                enabled = has_unsaved_workspace_edits
            draw_grid_button(rect, label, action, active=active, color=color,
                             enabled=enabled or action in ("toggle_edit", "toggle_help"))

        edit_mode_live = bool(getattr(self, 'live_mod_apply', True))
        pending_apply = int(self._pending_mod_apply_count()) if hasattr(self, '_pending_mod_apply_count') else 0
        mode_row_y = by + 2 * row_stride
        mode_buttons = [
            ("Auto Save", "apply_mode_live", edit_mode_live, OK_COLOR, True),
            ("Manual", "apply_mode_batch", not edit_mode_live, WARN_COLOR, True),
        ]
        for idx, (label, action, active, color, enabled) in enumerate(mode_buttons):
            rect = pygame.Rect(card.x + pad + idx * (btn_w + 6), mode_row_y, btn_w, btn_h)
            draw_grid_button(rect, label, action, active=active, color=color, enabled=enabled)

        apply_row_y = by + 3 * row_stride
        apply_label = "Apply Pending" if pending_apply else "No Pending Apply"
        apply_rect = pygame.Rect(card.x + pad, apply_row_y, card.width - pad * 2, btn_h)
        draw_grid_button(
            apply_rect,
            apply_label,
            "apply_pending_mod",
            active=False,
            color=ACCENT2_COLOR,
            enabled=pending_apply > 0,
        )

        mini_h = 92
        mini_y = side_rect.bottom - mini_h - 10
        if self.tiles and mini_y > card.bottom + 8:
            pygame.draw.line(self.screen, (32, 46, 66), (side_rect.x + 10, mini_y - 8), (side_rect.right - 10, mini_y - 8), 1)
            draw_small_text(side_rect.x + 14, mini_y - 2, "Viewport", TEXT_COLOR)
            self._draw_minimap(self.screen, side_rect.x + 12, mini_y + 12, side_rect.width - 24, mini_h - 12)

    def _draw_tile_cleanup_tile_overlay(
            self, tile, sx, sy, disp_size, mouse_pos, preview_keys=None):
        if not getattr(self, 'tile_delete_mode', False):
            return
        cr = pygame.Rect(int(sx), int(sy), disp_size, disp_size)
        key = f'{tile.x},{tile.y}'
        selected = key in getattr(self, 'tile_delete_selection', set())
        if preview_keys is None:
            preview_keys = self._tile_cleanup_preview_keys()
        preview = key in preview_keys
        operation = getattr(self, 'tile_delete_drag_operation', 'replace')
        if selected:
            tint = pygame.Surface((disp_size, disp_size), pygame.SRCALPHA)
            tint.fill((225, 48, 48, 105))
            self.screen.blit(tint, (int(sx), int(sy)))
            pygame.draw.rect(self.screen, (255, 82, 72), cr, 3)
        else:
            pygame.draw.rect(self.screen, (75, 86, 98), cr, 1)
        if preview:
            preview_surf = pygame.Surface(
                (disp_size, disp_size), pygame.SRCALPHA
            )
            if operation == 'subtract':
                preview_surf.fill((45, 210, 130, 95))
                preview_color = (85, 255, 170)
            else:
                preview_surf.fill((255, 176, 45, 90))
                preview_color = (255, 200, 70)
            self.screen.blit(preview_surf, (int(sx), int(sy)))
            pygame.draw.rect(self.screen, preview_color, cr, 3)
        elif cr.collidepoint(*mouse_pos):
            pygame.draw.rect(self.screen, (255, 225, 120), cr, 3)


    def _draw_terrain(self, w, h, content_top, ts, draw_min_x, draw_max_x, draw_min_y, draw_max_y):
        """Grid, tile images, OSM overlay, generation placeholders."""
        cleanup_preview_keys = (
            self._tile_cleanup_preview_keys()
            if getattr(self, 'tile_delete_mode', False)
            else set()
        )
        # ---- Grid ----
        if ts > 8:
            for tx in range(draw_min_x, draw_max_x + 2):
                sx = (tx - self.min_x) * ts + self.pan_x
                if 0 <= sx <= w:
                    pygame.draw.line(self.screen, BORDER_COLOR,
                                     (int(sx), content_top), (int(sx), h - STATUS_H), 1)
            for ty in range(draw_min_y, draw_max_y + 2):
                sy = (self.max_y - ty + 1) * ts + self.pan_y
                if content_top <= sy <= h - STATUS_H:
                    pygame.draw.line(self.screen, BORDER_COLOR,
                                     (0, int(sy)), (w, int(sy)), 1)

        # ---- Fill empty cells within loaded bounds ----
        # Only fill cells that are gaps in the *loaded* tileset, not generation placeholders.
        # Queued/running tiles are handled separately below with their own colour.
        if ts > 8:
            gen_active_coords = set(self.gen_running.keys()) | self.gen_queue
            for ty in range(draw_min_y, draw_max_y + 1):
                for tx in range(draw_min_x, draw_max_x + 1):
                    key = f'{tx},{ty}'
                    if key not in self.tiles and (tx, ty) not in gen_active_coords:
                        sx, sy = self.tile_screen_pos(tx, ty)
                        disp = int(ts)
                        if sx > w or sy > h or sx+disp < 0 or sy+disp < content_top:
                            continue
                        pygame.draw.rect(self.screen, MISSING_COLOR,
                                         (int(sx)+1, int(sy)+1, disp-2, disp-2))

        # ---- Tiles ----
        mx0, my0 = pygame.mouse.get_pos()
        disp_size = int(ts)
        visible_tiles = []
        view_cx = w * 0.5
        view_cy = (content_top + (h - STATUS_H)) * 0.5
        for tile in list(self.tiles.values()):
            sx, sy = self.tile_screen_pos(tile.x, tile.y)
            if sx > w or sy > h or sx + disp_size < 0 or sy + disp_size < content_top:
                continue
            dx = (sx + disp_size * 0.5) - view_cx
            dy = (sy + disp_size * 0.5) - view_cy
            visible_tiles.append((dx * dx + dy * dy, tile, sx, sy))

        visible_tiles.sort(key=lambda item: item[0])
        cache_budget = min(len(visible_tiles), 4)
        if (self.loading
                or getattr(self, 'dragging', False)
                or getattr(self, 'painting', False)
                or getattr(self, 'dragging_spliney_pt', False)):
            cache_budget = min(cache_budget, 2)
        pending_visible = 0

        for _priority, tile, sx, sy in visible_tiles:
            surf2 = tile.peek_overview(self.mode, self.hillshade)
            if surf2 is None and cache_budget > 0:
                surf2 = tile.get_overview(self.mode, self.hillshade)
                cache_budget -= 1
            if surf2 is None:
                pending_visible += 1
                pygame.draw.rect(
                    self.screen,
                    (18, 24, 34),
                    (int(sx) + 1, int(sy) + 1, max(1, disp_size - 2), max(1, disp_size - 2)),
                )
                pygame.draw.rect(
                    self.screen,
                    (48, 62, 82),
                    (int(sx) + 1, int(sy) + 1, max(1, disp_size - 2), max(1, disp_size - 2)),
                    1,
                )
                self._draw_tile_cleanup_tile_overlay(
                    tile, sx, sy, disp_size, (mx0, my0),
                    cleanup_preview_keys,
                )
                continue
            scaled = tile.get_scaled_overview(self.mode, self.hillshade, disp_size, allow_render=False)
            self.screen.blit(scaled, (int(sx), int(sy)))

            # Red = marked for deletion; green = keep/remove preview; amber = add.
            self._draw_tile_cleanup_tile_overlay(
                tile, sx, sy, disp_size, (mx0, my0),
                cleanup_preview_keys,
            )

            # Diff overlay: tint modified tiles orange-amber
            if self.diff_mode and tile.dirty and disp_size > 4:
                diff_surf = pygame.Surface((disp_size, disp_size), pygame.SRCALPHA)
                diff_surf.fill((255, 120, 20, 60))
                self.screen.blit(diff_surf, (int(sx), int(sy)))
                pygame.draw.rect(self.screen, (255, 140, 40),
                                 (int(sx), int(sy), disp_size, disp_size), 2)

            if tile.dirty and not self.diff_mode:
                pygame.draw.circle(self.screen, WARN_COLOR, (int(sx) + 8, int(sy) + 8), 5)
                pygame.draw.circle(self.screen, (0,0,0),    (int(sx) + 8, int(sy) + 8), 5, 1)

            # Tile coord label at high zoom
            if disp_size > 80:
                lbl = f"{tile.x},{tile.y}"
                lr, _ = self.font.render(lbl, DIM_COLOR)
                self.screen.blit(lr, (int(sx) + disp_size - lr.get_width() - 4,
                                      int(sy) + disp_size - lr.get_height() - 4))

        self._tile_cache_visible_pending = pending_visible

        # ---- OSM overlay ----
        self.osm.draw(self.screen, self, content_top, self._osm_bounds)

        # ---- Generation placeholders (queued/running tiles not yet loaded) ----
        if self.gen_queue or self.gen_running:
            for (gx, gy), status in list(self.gen_running.items()):
                if f'{gx},{gy}' in self.tiles:
                    continue  # already loaded
                sx, sy = self.tile_screen_pos(gx, gy)
                disp_size = int(ts)
                if sx > w or sy > h or sx + disp_size < 0 or sy + disp_size < content_top:
                    continue
                # Animated amber shimmer for running tiles
                pygame.draw.rect(self.screen, (40, 32, 10),
                                 (int(sx)+1, int(sy)+1, disp_size-2, disp_size-2))
                pygame.draw.rect(self.screen, (120, 90, 20),
                                 (int(sx)+1, int(sy)+1, disp_size-2, disp_size-2), 1)
                if disp_size > 40:
                    short = status[:16]
                    self.font.render_to(self.screen,
                                        (int(sx)+4, int(sy)+4), short, (180, 140, 40))
            for (gx, gy) in list(self.gen_queue):
                if f'{gx},{gy}' in self.tiles or (gx, gy) in self.gen_running:
                    continue
                sx, sy = self.tile_screen_pos(gx, gy)
                disp_size = int(ts)
                if sx > w or sy > h or sx + disp_size < 0 or sy + disp_size < content_top:
                    continue
                # Subtle blue tint for queued tiles
                pygame.draw.rect(self.screen, (20, 30, 50),
                                 (int(sx)+1, int(sy)+1, disp_size-2, disp_size-2))
                pygame.draw.rect(self.screen, (40, 70, 110),
                                 (int(sx)+1, int(sy)+1, disp_size-2, disp_size-2), 1)


    def _draw_track_overlay(self, w, h, content_top):
        """Track nodes, segments, splineys, areas, bridge cars."""
        def elev_color_from_ratio(t):
            t = max(0.0, min(1.0, t))
            if t < 0.25:
                tt = t / 0.25
                return (0, int(tt * 200), int(255 - tt * 55))
            if t < 0.5:
                tt = (t - 0.25) / 0.25
                return (0, int(200 + tt * 55), int(200 - tt * 200))
            if t < 0.75:
                tt = (t - 0.5) / 0.25
                return (int(tt * 255), 255, 0)
            tt = (t - 0.75) / 0.25
            return (255, int(255 - tt * 255), 0)

        def grade_color(grade):
            if abs(grade) >= 3.0:
                return (255, 100, 80)
            if abs(grade) >= 1.5:
                return (255, 220, 80)
            return (120, 255, 160)
        # ---- Track overlay ----
        if self.show_tracks and self.track_segments and self.mod_project and self.track_graph_path:
            ref_color = (90, 140, 190)
            ref_outline = (12, 20, 30)
            for idx, (curve, _tc) in enumerate(self.track_segments):
                screen_pts = [self.unity_to_screen(x2, z2) for x2, z2 in curve]
                if all(spx < -50 or spx > w + 50 or spy < content_top - 50 or spy > h + 50
                       for spx, spy in screen_pts):
                    continue
                for i in range(len(screen_pts) - 1):
                    pygame.draw.line(self.screen, ref_outline, screen_pts[i], screen_pts[i + 1], 2)
                for i in range(len(screen_pts) - 1):
                    pygame.draw.line(self.screen, ref_color, screen_pts[i], screen_pts[i + 1], 1)
                seg_meta = self.track_segment_meta[idx] if idx < len(self.track_segment_meta) else None
                _draw_segment_style_overlay(
                    self.screen,
                    screen_pts,
                    seg_meta.get('style') if seg_meta else None,
                    self.zoom,
                    self.font,
                )

            if self.show_nodes and self.track_node_list:
                node_r = max(2, int(self.zoom * 0.25))
                for nx2, nz2, _nid in self.track_node_list:
                    spx, spy = self.unity_to_screen(nx2, nz2)
                    if spx < -20 or spx > w + 20 or spy < content_top - 20 or spy > h + 20:
                        continue
                    pygame.draw.circle(self.screen, ref_outline, (spx, spy), node_r + 1)
                    pygame.draw.circle(self.screen, ref_color, (spx, spy), node_r)

        # Mod project layer rendering
        if self.mod_project and not self.mod_panel:
            for li, layer in enumerate(self.mod_project.layers):
                if not layer.visible:
                    continue
                lcol = layer.color

                # --- Node dots ---
                if self.show_nodes:
                    node_r = max(2, int(self.zoom * 0.3))

                    # Pre-compute Y range for elevation color coding
                    visible_ys = []
                    if self.show_elev_colors:
                        for node in layer.nodes.values():
                            if node.get('deleted'):
                                continue
                            snx, sny = self.unity_to_screen(node['x'], node['z'])
                            if -20 <= snx <= w + 20 and content_top - 20 <= sny <= h + 20:
                                visible_ys.append(node['y'])
                        y_min = min(visible_ys) if visible_ys else 0.0
                        y_max = max(visible_ys) if visible_ys else 1.0
                        y_span = max(y_max - y_min, 0.1)

                    for nid, node in layer.nodes.items():
                        if node.get('deleted'):
                            continue
                        snx, sny = self.unity_to_screen(node['x'], node['z'])
                        if snx < -20 or snx > w+20 or sny < content_top-20 or sny > h+20:
                            continue

                        # Elevation color: blue(low)ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢cyanÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢greenÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢yellowÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢red(high)
                        if self.show_elev_colors and visible_ys:
                            t = (node['y'] - y_min) / y_span  # 0..1
                            dot_col = elev_color_from_ratio(t)
                        else:
                            dot_col = lcol
                        pygame.draw.circle(self.screen, dot_col, (snx, sny), node_r+1)
                        pygame.draw.circle(self.screen, (255,255,255), (snx, sny), node_r, 1)

                        if self.zoom > 0.3:
                            self.font.render_to(self.screen, (snx+4, sny-8), nid, dot_col)
                            # Show Y elevation label when elev color mode is on
                            if self.show_elev_colors and self.zoom > 0.5:
                                y_lbl = f"{node['y']:.0f}m"
                                self.font.render_to(self.screen, (snx+4, sny+4),
                                                    y_lbl, (200, 240, 255))

                # --- Track segments (bezier curves) ---
                for pts, col, seg_id in layer.curves:
                    screen_pts = [self.unity_to_screen(x2, z2) for x2, z2 in pts]
                    if all(sx < -50 or sx > w+50 or sy < content_top-50 or sy > h+50
                           for sx, sy in screen_pts):
                        continue
                    for i in range(len(screen_pts)-1):
                        pygame.draw.line(self.screen, (0,0,0),
                                         screen_pts[i], screen_pts[i+1], 3)
                    for i in range(len(screen_pts)-1):
                        pygame.draw.line(self.screen, col,
                                         screen_pts[i], screen_pts[i+1], 1)
                    seg = (layer.segments.get(seg_id) or
                           self.mod_project.merged_segments.get(seg_id, {}))
                    _draw_segment_style_overlay(
                        self.screen,
                        screen_pts,
                        seg.get('style', 'Standard'),
                        self.zoom,
                        self.font,
                    )

                    # Grade % label at segment midpoint
                    if self.show_grade_labels and self.zoom > 0.15 and len(screen_pts) >= 2:
                        n0 = self.mod_project.merged_nodes.get(seg.get('startId', ''))
                        n1 = self.mod_project.merged_nodes.get(seg.get('endId', ''))
                        if n0 and n1:
                            import math as _mth
                            dx = n1['x'] - n0['x']
                            dz = n1['z'] - n0['z']
                            dist = _mth.sqrt(dx*dx + dz*dz)
                            if dist > 0.1:
                                grade = (n1['y'] - n0['y']) / dist * 100.0
                                mid = screen_pts[len(screen_pts) // 2]
                                g_lbl = f"{abs(grade):.1f}%"
                                g_col = grade_color(grade)
                                # shadow then label
                                self.font.render_to(self.screen,
                                    (mid[0]+1, mid[1]+1), g_lbl, (0, 0, 0))
                                self.font.render_to(self.screen,
                                    (mid[0], mid[1]), g_lbl, g_col)

                # --- Splineys ---
                for spl_id, spl in layer.splineys.items():
                    if not spl:
                        continue
                    handler = spl.get('handler', '')

                    # Rivers / Roads (FlowyThingBuilder) ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â polyline through points
                    if 'FlowyThing' in handler:
                        pts = [pt for pt in spl.get('points', []) if isinstance(pt, dict)]
                        if len(pts) < 2:
                            continue
                        style = spl.get('style', '')
                        river_col = (60, 120, 255) if style == 'River' else (180, 140, 80)
                        river_curve = []
                        for i in range(len(pts) - 1):
                            curve = _flowy_segment_points(pts[i], pts[i + 1])
                            if len(curve) < 2:
                                continue
                            screen_curve = [self.unity_to_screen(x2, z2) for x2, z2 in curve]
                            if style == 'River':
                                if river_curve:
                                    river_curve.extend(screen_curve[1:])
                                else:
                                    river_curve.extend(screen_curve)
                            width0 = float(pts[i].get('width', 10.0) or 10.0)
                            width1 = float(pts[i + 1].get('width', width0) or width0)
                            for j in range(len(screen_curve) - 1):
                                p0 = screen_curve[j]
                                p1 = screen_curve[j + 1]
                                if (
                                    max(p0[0], p1[0]) < -100 or
                                    min(p0[0], p1[0]) > w + 100 or
                                    max(p0[1], p1[1]) < content_top - 100 or
                                    min(p0[1], p1[1]) > h + 100
                                ):
                                    continue
                                t = j / max(1, len(screen_curve) - 1)
                                width_m = width0 + (width1 - width0) * t
                                lw = max(1, int(width_m * self.zoom / 500))
                                pygame.draw.line(self.screen, (0,0,0), p0, p1, lw + 2)
                                pygame.draw.line(self.screen, river_col, p0, p1, max(1, lw))
                        if style == 'River':
                            _draw_flow_arrow(self.screen, river_curve, (220, 235, 255))
                        continue
                        sp = [self.unity_to_screen(
                                p['position']['x'], p['position']['z'])
                              for p in pts]
                        # Width-scaled lines (width in metres ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ screen pixels at zoom)
                        for i in range(len(sp)-1):
                            if sp[i][0] < -100 or sp[i][0] > w+100: continue
                            w_m = pts[i].get('width', 10.0)
                            lw  = max(1, int(w_m * self.zoom / 500))
                            pygame.draw.line(self.screen, (0,0,0),
                                             sp[i], sp[i+1], lw+2)
                            pygame.draw.line(self.screen, river_col,
                                             sp[i], sp[i+1], max(1, lw))

                    # AutoTrestle ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â draw as a thick grey line between endpoints
                    elif 'AutoTrestle' in handler:
                        pts = [pt for pt in spl.get('points', []) if isinstance(pt, dict)]
                        if len(pts) < 2:
                            continue
                        trestle_curve = []
                        for i in range(len(pts) - 1):
                            # New trestles contain dense samples taken directly
                            # from the rail segment's 3D Bezier. Draw those
                            # samples as the centerline so the desktop preview
                            # cannot bow away from the rail by applying another
                            # independent spline. Preserve cubic rendering for
                            # legacy two-point trestles.
                            if len(pts) > 2:
                                pos0 = pts[i].get('position', {}) or {}
                                pos1 = pts[i + 1].get('position', {}) or {}
                                curve = [
                                    (
                                        float(pos0.get('x', 0.0)),
                                        float(pos0.get('z', 0.0)),
                                    ),
                                    (
                                        float(pos1.get('x', 0.0)),
                                        float(pos1.get('z', 0.0)),
                                    ),
                                ]
                            else:
                                curve = _flowy_segment_points(pts[i], pts[i + 1])
                            if len(curve) < 2:
                                continue
                            screen_curve = [self.unity_to_screen(x2, z2) for x2, z2 in curve]
                            if trestle_curve:
                                trestle_curve.extend(screen_curve[1:])
                            else:
                                trestle_curve = list(screen_curve)
                            for p0, p1 in zip(screen_curve, screen_curve[1:]):
                                pygame.draw.line(self.screen, (20, 20, 20), p0, p1, 4)
                                pygame.draw.line(self.screen, (95, 95, 95), p0, p1, 3)
                                pygame.draw.line(self.screen, (208, 198, 156), p0, p1, 1)
                        if self.zoom > 0.28 and trestle_curve:
                            mid = trestle_curve[len(trestle_curve) // 2]
                            self.font.render_to(self.screen, (mid[0] + 4, mid[1] - 10), "Trestle", (208, 198, 156))

                    # MapLabel ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â text at position
                    elif 'MapLabel' in handler:
                        px2 = spl.get('position', {})
                        sx, sy = self.unity_to_screen(
                            px2.get('x', 0), px2.get('z', 0))
                        if -20 < sx < w+20 and content_top < sy < h:
                            txt = spl.get('text', '')
                            if self.zoom > 0.05:
                                self.font.render_to(self.screen,
                                    (sx+2, sy+2), txt, (0,0,0))
                                self.font.render_to(self.screen,
                                    (sx, sy), txt, (255, 240, 180))

                    # Turntable ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â circle
                    elif 'Turntable' in handler:
                        px2 = spl.get('position', {})
                        sx, sy = self.unity_to_screen(
                            px2.get('x', 0), px2.get('z', 0))
                        if -20 < sx < w+20 and content_top < sy < h:
                            r2 = max(3, int(self.zoom * 0.3))
                            pygame.draw.circle(self.screen, (80,80,80),
                                               (sx,sy), r2+2)
                            pygame.draw.circle(self.screen, (200,160,80),
                                               (sx,sy), r2)

                    # Station / Loader ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â small diamond
                    elif 'Station' in handler or 'Loader' in handler:
                        px2 = spl.get('position', {})
                        sx, sy = self.unity_to_screen(
                            px2.get('x', 0), px2.get('z', 0))
                        if -20 < sx < w+20 and content_top < sy < h:
                            r2 = max(4, int(self.zoom * 0.5))
                            pts2 = [(sx,sy-r2),(sx+r2,sy),(sx,sy+r2),(sx-r2,sy)]
                            col2 = (80,220,120) if 'Station' in handler                                    else (220,160,80)
                            pygame.draw.polygon(self.screen, (0,0,0), pts2)
                            pygame.draw.polygon(self.screen, col2, pts2)
                            pygame.draw.polygon(self.screen, (255,255,255),
                                                pts2, 1)

                # --- Scenery (buildings) ---
                if self.zoom > 0.1:  # only show at reasonable zoom
                    for sc_id, sc in layer.scenery.items():
                        if not sc:
                            continue
                        px2 = sc.get('position', {})
                        sx, sy = self.unity_to_screen(
                            px2.get('x', 0), px2.get('z', 0))
                        if sx < -10 or sx > w+10 or sy < content_top or sy > h:
                            continue
                        r2 = max(2, int(self.zoom * 0.2))
                        pygame.draw.rect(self.screen, (0,0,0),
                                         (sx-r2-1, sy-r2-1, r2*2+2, r2*2+2))
                        pygame.draw.rect(self.screen, lcol,
                                         (sx-r2, sy-r2, r2*2, r2*2))
                        if self.zoom > 0.4:
                            mdl = sc.get('modelIdentifier', '')
                            self.font.render_to(self.screen,
                                (sx+r2+2, sy-6), mdl, lcol)

                # --- Spliney control point dots (when zoomed in enough) ---
                if self.zoom > 0.1:
                    for sid2, spl in layer.splineys.items():
                        if not spl or 'points' not in spl:
                            continue
                        for pi, pt in enumerate(spl['points']):
                            if not isinstance(pt, dict):
                                continue
                            pos2 = pt.get('position', {})
                            spx, spy = self.unity_to_screen(
                                pos2.get('x',0), pos2.get('z',0))
                            if spx < -10 or spx > w+10 or spy < content_top or spy > h:
                                continue
                            is_anchor = (
                                sid2 == getattr(self, 'sel_spliney_range_id', None) and
                                li == getattr(self, 'sel_spliney_range_layer', None) and
                                pi == getattr(self, 'sel_spliney_range_anchor', -1)
                            )
                            is_sel = (sid2 == self.sel_spliney_id and
                                      pi  == self.sel_spliney_pt)
                            r_sp  = 5 if is_sel else (4 if is_anchor else 3)
                            col_sp = (0,220,255) if is_sel else ((255,180,90) if is_anchor else (200,200,100))
                            pygame.draw.circle(self.screen,(0,0,0),(spx,spy),r_sp+2)
                            pygame.draw.circle(self.screen,col_sp,(spx,spy),r_sp)
                            if is_anchor and not is_sel:
                                pygame.draw.circle(self.screen,(255,245,220),(spx,spy),r_sp,1)
                            if is_sel:
                                pygame.draw.circle(self.screen,(255,255,255),(spx,spy),r_sp,1)

                # --- Area centres (towns) ---
                for area_id, area in layer.areas.items():
                    if not area:
                        continue
                    px2 = area.get('position', {})
                    sx, sy = self.unity_to_screen(
                        px2.get('x', 0), px2.get('z', 0))
                    if sx < -20 or sx > w+20 or sy < content_top or sy > h:
                        continue
                    r2 = max(5, int(self.zoom * 0.6))
                    pygame.draw.circle(self.screen, (0,0,0), (sx,sy), r2+2)
                    pygame.draw.circle(self.screen, lcol, (sx,sy), r2)
                    pygame.draw.circle(self.screen, (255,255,255), (sx,sy), r2, 1)
                    if self.zoom > 0.05:
                        name = area.get('name', area_id)
                        self.font.render_to(self.screen,
                            (sx+r2+3, sy-6), name, lcol)

        # Mod panel overlay (drawn on top)
        if self.mod_panel and _MOD_AVAILABLE:
            self._draw_mod_panel(self.screen, content_top)

        # Progression editor panel
        if self.prog_panel and _MOD_AVAILABLE:
            self._draw_progression_panel(self.screen, content_top)

        # Area editor panel
        if self.area_panel and _MOD_AVAILABLE:
            self._draw_area_panel(self.screen, content_top)

        # Spans editor panel
        if self.span_panel and _MOD_AVAILABLE:
            self._draw_spans_panel(self.screen, content_top)

        # Scenery placement panel
        if self.scenery_panel and _MOD_AVAILABLE:
            self._draw_scenery_panel(self.screen, content_top)

        # Group move panel + rubber band
        if _MOD_AVAILABLE and self.mod_project:
            self._draw_group_rubber_band()
        if self.group_panel and _MOD_AVAILABLE:
            self._draw_group_panel(self.screen, content_top)

        # Calculator panel
        if self.calc_panel:
            self._draw_calc_panel(self.screen, content_top)

        # Mandela panel
        if self.mandela_panel and _MOD_AVAILABLE:
            self._draw_mandela_panel(self.screen, content_top)

        # Spliney point properties
        if self.sel_spliney_id and not self.mod_panel and _MOD_AVAILABLE:
            self._draw_spliney_props(self.screen, content_top)

        # Geometry tools panel
        if self.geo_panel and _MOD_AVAILABLE:
            self._draw_geo_panel(self.screen, content_top)

        if self.show_tracks and self.track_segments and not self.mod_project:
            for idx, (curve, tc) in enumerate(self.track_segments):
                screen_pts = [self.unity_to_screen(x2, z2) for x2, z2 in curve]
                if all(spx < -50 or spx > w+50 or spy < content_top-50 or spy > h+50
                       for spx, spy in screen_pts):
                    continue
                color = self.track_colors.get(tc, self.track_default_color)
                for i in range(len(screen_pts)-1):
                    pygame.draw.line(self.screen, (0,0,0), screen_pts[i], screen_pts[i+1], 3)
                for i in range(len(screen_pts)-1):
                    pygame.draw.line(self.screen, color, screen_pts[i], screen_pts[i+1], 1)

                seg_meta = self.track_segment_meta[idx] if idx < len(self.track_segment_meta) else None
                _draw_segment_style_overlay(
                    self.screen,
                    screen_pts,
                    seg_meta.get('style') if seg_meta else None,
                    self.zoom,
                    self.font,
                )
                grade = seg_meta.get('grade_pct') if seg_meta else None
                if self.show_grade_labels and self.zoom > 0.15 and len(screen_pts) >= 2 and grade is not None:
                    mid = screen_pts[len(screen_pts) // 2]
                    g_lbl = f"{abs(grade):.1f}%"
                    g_col = grade_color(grade)
                    self.font.render_to(self.screen, (mid[0] + 1, mid[1] + 1), g_lbl, (0, 0, 0))
                    self.font.render_to(self.screen, (mid[0], mid[1]), g_lbl, g_col)

        if self.show_tracks and self.show_nodes and self.track_node_list and not self.mod_project:
            node_r = max(2, int(self.zoom * 0.3))
            visible_ys = []
            if self.show_elev_colors:
                for nx2, nz2, nid in self.track_node_list:
                    spx, spy = self.unity_to_screen(nx2, nz2)
                    if -20 <= spx <= w + 20 and content_top - 20 <= spy <= h + 20:
                        visible_ys.append(self.track_node_elevs.get(nid, 0.0))
                y_min = min(visible_ys) if visible_ys else 0.0
                y_max = max(visible_ys) if visible_ys else 1.0
                y_span = max(y_max - y_min, 0.1)
            for nx2, nz2, nid in self.track_node_list:
                spx, spy = self.unity_to_screen(nx2, nz2)
                if spx < -20 or spx > w+20 or spy < content_top-20 or spy > h+20:
                    continue
                if self.show_elev_colors and visible_ys:
                    y_val = self.track_node_elevs.get(nid, 0.0)
                    dot_col = elev_color_from_ratio((y_val - y_min) / y_span)
                else:
                    dot_col = (255, 255, 255)
                pygame.draw.circle(self.screen, dot_col, (spx, spy), node_r + 1)
                pygame.draw.circle(self.screen, (255, 255, 255), (spx, spy), node_r, 1)
                if self.zoom > 0.3:
                    self.font.render_to(self.screen, (spx+4, spy-8), nid, dot_col)
                    if self.show_elev_colors and self.zoom > 0.5:
                        y_lbl = f"{self.track_node_elevs.get(nid, 0.0):.0f}m"
                        self.font.render_to(self.screen, (spx+4, spy+4), y_lbl, (200, 240, 255))


    def _draw_selection_highlight(self, w, h, content_top):
        """Selection rectangle highlight."""
        # Scenery is represented by an origin/heading marker because the editor
        # does not have access to the game's rendered prefab meshes.
        scenery_id = getattr(self, 'sel_scenery_id', None)
        if scenery_id and self.mod_project:
            scenery = self.mod_project.merged_scenery.get(scenery_id)
            if isinstance(scenery, dict):
                position = scenery.get('position', {}) or {}
                rotation = scenery.get('rotation', {}) or {}
                scale = scenery.get('scale', {}) or {}
                sx, sy = self.unity_to_screen(
                    float(position.get('x', 0.0)),
                    float(position.get('z', 0.0)),
                )
                hidden_by_panel = False
                if getattr(self, 'scenery_panel', False):
                    panel_w = min(w - 40, 860)
                    panel_h = min(h - content_top - STATUS_H - 20, 560)
                    panel_rect = pygame.Rect(
                        (w - panel_w) // 2,
                        content_top + 10,
                        panel_w,
                        panel_h,
                    )
                    hidden_by_panel = panel_rect.collidepoint(sx, sy)
                if (
                    not hidden_by_panel
                    and -30 <= sx <= w + 30
                    and content_top - 30 <= sy <= h + 30
                ):
                    uniform_scale = float(scale.get('x', 1.0))
                    marker_r = max(7, min(16, int(7 + uniform_scale * 2)))
                    diamond = [
                        (sx, sy - marker_r),
                        (sx + marker_r, sy),
                        (sx, sy + marker_r),
                        (sx - marker_r, sy),
                    ]
                    pygame.draw.polygon(self.screen, (0, 0, 0), diamond)
                    pygame.draw.polygon(self.screen, (210, 120, 255), diamond, 3)
                    pygame.draw.circle(self.screen, (255, 255, 255), (sx, sy), 3)

                    rot_y = float(rotation.get('y', 0.0))
                    angle = math.radians(rot_y)
                    tip = (
                        int(sx + math.sin(angle) * 25),
                        int(sy - math.cos(angle) * 25),
                    )
                    pygame.draw.line(
                        self.screen, (255, 255, 255), (sx, sy), tip, 4
                    )
                    pygame.draw.line(
                        self.screen, (210, 120, 255), (sx, sy), tip, 2
                    )
                    model = scenery.get('modelIdentifier', '?')
                    self.font.render_to(
                        self.screen,
                        (sx + marker_r + 5, sy - marker_r),
                        f"{model}  {scenery_id}",
                        (230, 190, 255),
                    )

        # ---- Selection highlight + properties panel ----
        if (self.sel_mod_node_id or self.sel_mod_seg_id) and not self.mod_panel:
            li    = self.sel_mod_layer_idx
            is_mod = li is not None and self.mod_project is not None

            if self.sel_mod_node_id:
                if is_mod:
                    layer = self.mod_project.layers[li]
                    node  = layer.nodes.get(self.sel_mod_node_id) or                             self.mod_project.merged_nodes.get(self.sel_mod_node_id)
                else:
                    node_state = self._get_track_node_state(self.sel_mod_node_id)
                    node = {'id': node_state['id'], 'x': node_state['x'], 'y': node_state['y'], 'z': node_state['z'],
                            'rotX': 0, 'rotY': node_state.get('rotY', 0.0), 'rotZ': 0,
                            'flipSwitchStand': False, 'source': node_state.get('source', 'loaded')} if node_state else None
                    layer = None
                if node and not node.get('deleted'):
                    sx2, sy2 = self.unity_to_screen(node['x'], node['z'])
                    r3 = max(6, int(self.zoom * 0.5))
                    pygame.draw.circle(self.screen, (0,0,0),      (sx2,sy2), r3+4)
                    pygame.draw.circle(self.screen, (255,255,255), (sx2,sy2), r3+3, 2)
                    pygame.draw.circle(self.screen, (0,200,255),   (sx2,sy2), r3+1, 2)
                    pass  # properties drawn by _draw_properties_panel below

            elif self.sel_mod_seg_id:
                if is_mod:
                    layer = self.mod_project.layers[li]
                    seg   = layer.segments.get(self.sel_mod_seg_id) or                             self.mod_project.merged_segments.get(self.sel_mod_seg_id, {})
                    for pts, col, cid in layer.curves:
                        if cid == self.sel_mod_seg_id:
                            screen_pts = [self.unity_to_screen(x2, z2) for x2, z2 in pts]
                            for i in range(len(screen_pts)-1):
                                pygame.draw.line(self.screen, (255,255,255),
                                                 screen_pts[i], screen_pts[i+1], 4)
                                pygame.draw.line(self.screen, (0,200,255),
                                                 screen_pts[i], screen_pts[i+1], 2)
                            break
                else:
                    seg = self._get_track_segment_state(self.sel_mod_seg_id) or {}
                    layer = None
                    pts = self.track_segment_points.get(self.sel_mod_seg_id)
                    if pts:
                        screen_pts = [self.unity_to_screen(x2, z2) for x2, z2 in pts]
                        for i in range(len(screen_pts)-1):
                            pygame.draw.line(self.screen, (255,255,255),
                                             screen_pts[i], screen_pts[i+1], 4)
                            pygame.draw.line(self.screen, (0,200,255),
                                             screen_pts[i], screen_pts[i+1], 2)
                pass  # properties drawn by _draw_properties_panel below

        # ---- Properties panel ----
        if (self.sel_mod_node_id or self.sel_mod_seg_id) and not self.mod_panel:
            self._draw_properties_panel(self.screen, content_top)


    def _draw_geo_preview(self, w, h, content_top):
        """Geometry ghost nodes/segments before commit."""
        guide_points = list(self._alignment_guide_points_xz())
        source = self._alignment_source_chain() if self.mod_project else None
        source_points = self._alignment_source_points(source)
        source_polyline = self._alignment_source_polyline(source)

        if source_points and self.geo_panel and self.geo_mode in ('guide', 'fit_arc'):
            for idx in range(len(source_points) - 1):
                sx0, sy0 = self.unity_to_screen(source_points[idx][0], source_points[idx][1])
                sx1, sy1 = self.unity_to_screen(source_points[idx + 1][0], source_points[idx + 1][1])
                pygame.draw.line(self.screen, (60, 120, 170), (sx0, sy0), (sx1, sy1), 2)

        if guide_points:
            for idx in range(len(guide_points) - 1):
                sx0, sy0 = self.unity_to_screen(guide_points[idx][0], guide_points[idx][1])
                sx1, sy1 = self.unity_to_screen(guide_points[idx + 1][0], guide_points[idx + 1][1])
                pygame.draw.line(self.screen, (0, 220, 180), (sx0, sy0), (sx1, sy1), 2)
            for idx, point in enumerate(guide_points, start=1):
                sx, sy = self.unity_to_screen(point[0], point[1])
                if -12 < sx < w + 12 and content_top - 12 < sy < h + 12:
                    pygame.draw.circle(self.screen, (0, 0, 0), (sx, sy), 6)
                    pygame.draw.circle(self.screen, (0, 220, 180), (sx, sy), 5)
                    pygame.draw.circle(self.screen, (255, 255, 255), (sx, sy), 5, 1)
                    if self.zoom > 0.3:
                        self.font.render_to(self.screen, (sx + 7, sy - 8), str(idx), (0, 220, 180))

        if guide_points and source_polyline and self.geo_panel and self.geo_mode == 'guide':
            deviation = self._alignment_current_deviation(source)
            for idx, sample in enumerate(deviation.get('samples', [])):
                fx, fz = sample.get('from_point', (0.0, 0.0))
                tx, tz = sample.get('to_point', (0.0, 0.0))
                sx0, sy0 = self.unity_to_screen(fx, fz)
                sx1, sy1 = self.unity_to_screen(tx, tz)
                pygame.draw.line(self.screen, (255, 210, 100), (sx0, sy0), (sx1, sy1), 1)
                if idx % 2 == 0 and self.zoom > 0.35:
                    mx = (sx0 + sx1) // 2
                    my = (sy0 + sy1) // 2
                    self.font.render_to(
                        self.screen,
                        (mx + 5, my - 8),
                        f"{sample.get('distance', 0.0):.1f}m",
                        (255, 210, 100),
                    )

        if source_points and self.geo_panel and self.geo_mode in ('guide', 'fit_arc'):
            for sample in self._alignment_current_radius_warnings(source):
                px, pz = sample.get('point', (0.0, 0.0))
                sx, sy = self.unity_to_screen(px, pz)
                pygame.draw.circle(self.screen, (120, 0, 0), (sx, sy), 8)
                pygame.draw.circle(self.screen, (255, 90, 90), (sx, sy), 7, 2)
                self.font.render_to(
                    self.screen,
                    (sx + 10, sy - 8),
                    f"R {sample.get('radius', 0.0):.0f}",
                    (255, 110, 90),
                )

        if self.geo_preview and not self.mod_panel:
            preview_class_cols = {
                'Mainline': (255, 200, 0),
                'Branch': (255, 140, 0),
                'Industrial': (200, 80, 255),
            }
            for entry in self.geo_preview:
                nodes = entry[0]
                segs = entry[1]
                update_nodes = entry[2] if len(entry) > 2 else []
                lookup = {n['id']: n for n in nodes}
                lookup.update({n['id']: n for n in update_nodes})

                for seg in segs:
                    n0 = lookup.get(seg['startId'])
                    n1 = lookup.get(seg['endId'])
                    if not n0 and self.mod_project:
                        n0 = self.mod_project.merged_nodes.get(seg['startId'])
                    if not n1 and self.mod_project:
                        n1 = self.mod_project.merged_nodes.get(seg['endId'])
                    if not n0 or not n1:
                        continue
                    sx0, sy0 = self.unity_to_screen(n0['x'], n0['z'])
                    sx1, sy1 = self.unity_to_screen(n1['x'], n1['z'])
                    seg_col = preview_class_cols.get(seg.get('trackClass', ''), (255, 180, 0))
                    pygame.draw.line(self.screen, (0, 0, 0), (sx0, sy0), (sx1, sy1), 3)
                    pygame.draw.line(self.screen, seg_col, (sx0, sy0), (sx1, sy1), 2)
                    if (
                        seg.get('trackClass') not in ('Mainline', 'Branch')
                        or (self.geo_mode == 'turnout' and seg_col != (255, 200, 0))
                    ):
                        mid_sx = (sx0 + sx1) // 2
                        mid_sy = (sy0 + sy1) // 2
                        self.font.render_to(
                            self.screen,
                            (mid_sx + 4, mid_sy),
                            f"{seg.get('trackClass', '')} {seg.get('speedLimit', '')}mph",
                            seg_col,
                        )

                if update_nodes and len(update_nodes) >= 2:
                    for idx in range(len(update_nodes) - 1):
                        n0 = update_nodes[idx]
                        n1 = update_nodes[idx + 1]
                        sx0, sy0 = self.unity_to_screen(n0['x'], n0['z'])
                        sx1, sy1 = self.unity_to_screen(n1['x'], n1['z'])
                        pygame.draw.line(self.screen, (0, 0, 0), (sx0, sy0), (sx1, sy1), 4)
                        pygame.draw.line(self.screen, (0, 220, 255), (sx0, sy0), (sx1, sy1), 2)

                for idx, node in enumerate(nodes):
                    snx, sny = self.unity_to_screen(node['x'], node['z'])
                    if -10 < snx < w + 10 and content_top < sny < h:
                        pygame.draw.circle(self.screen, (0, 0, 0), (snx, sny), 5)
                        pygame.draw.circle(self.screen, (255, 180, 0), (snx, sny), 4)
                        pygame.draw.circle(self.screen, (255, 255, 255), (snx, sny), 4, 1)
                        if self.geo_mode == 'turnout' and self.zoom > 0.3:
                            roles = ['entry', 'through', 'diverge']
                            lbl = roles[idx] if idx < len(roles) else str(idx)
                            self.font.render_to(self.screen, (snx + 6, sny - 8), lbl, (200, 180, 140))

                for node in update_nodes:
                    snx, sny = self.unity_to_screen(node['x'], node['z'])
                    if -10 < snx < w + 10 and content_top < sny < h:
                        pygame.draw.circle(self.screen, (0, 0, 0), (snx, sny), 6)
                        pygame.draw.circle(self.screen, (0, 220, 255), (snx, sny), 5)
                        pygame.draw.circle(self.screen, (255, 255, 255), (snx, sny), 5, 1)

        radius_warning_samples = list(self.geo_preview_meta.get('radius_warnings', []))
        if not radius_warning_samples:
            radius_warning_samples = [
                sample for sample in self.geo_preview_meta.get('warnings', [])
                if isinstance(sample, dict)
            ]
        for sample in radius_warning_samples:
            px, pz = sample.get('point', (0.0, 0.0))
            sx, sy = self.unity_to_screen(px, pz)
            pygame.draw.circle(self.screen, (120, 0, 0), (sx, sy), 8)
            pygame.draw.circle(self.screen, (255, 90, 90), (sx, sy), 7, 2)
            self.font.render_to(
                self.screen,
                (sx + 10, sy - 8),
                f"R {sample.get('radius', 0.0):.0f}",
                (255, 110, 90),
            )

        if self.dragging_spliney_pt and self.sel_spliney_id:
            sx2, sy2 = self.drag_screen_pos
            pygame.draw.circle(self.screen,(0,0,0),(sx2,sy2),7)
            pygame.draw.circle(self.screen,(0,220,255),(sx2,sy2),6)
            pygame.draw.circle(self.screen,(255,255,255),(sx2,sy2),6,1)
            ux2,uz2 = self.screen_to_unity(sx2,sy2)
            uy2 = self._sample_terrain_y(ux2,uz2) or 0
            self.font.render_to(self.screen,(sx2+10,sy2-8),
                f"{self.sel_spliney_id}[{self.sel_spliney_pt}]  "
                f"({ux2:.1f},{uy2:.1f},{uz2:.1f})",(0,220,255))
        return
        # ---- Geometry preview (ghost nodes/segments before commit) ----
        if self.geo_preview and not self.mod_panel:
            for nodes, segs in self.geo_preview:
                # Draw ghost segments ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â colour by track class for turnout legibility
                PREVIEW_CLASS_COLS = {
                    'Mainline':   (255,200,0),
                    'Branch':     (255,140,0),
                    'Industrial': (200,80,255),
                }
                for s in segs:
                    n0 = next((n for n in nodes if n['id'] == s['startId']), None)
                    n1 = next((n for n in nodes if n['id'] == s['endId']), None)
                    if not n0 and self.mod_project:
                        n0d = self.mod_project.merged_nodes.get(s['startId'])
                        if n0d: n0 = n0d
                    if not n1 and self.mod_project:
                        n1d = self.mod_project.merged_nodes.get(s['endId'])
                        if n1d: n1 = n1d
                    if n0 and n1:
                        sx0, sy0 = self.unity_to_screen(n0['x'], n0['z'])
                        sx1, sy1 = self.unity_to_screen(n1['x'], n1['z'])
                        seg_col = PREVIEW_CLASS_COLS.get(s.get('trackClass',''), (255,180,0))
                        pygame.draw.line(self.screen, (0,0,0),    (sx0,sy0),(sx1,sy1), 3)
                        pygame.draw.line(self.screen, seg_col,    (sx0,sy0),(sx1,sy1), 2)
                        # Label diverging leg
                        if s.get('trackClass') not in ('Mainline','Branch') or                                 (self.geo_mode == 'turnout' and seg_col != (255,200,0)):
                            mid_sx = (sx0+sx1)//2; mid_sy = (sy0+sy1)//2
                            self.font.render_to(self.screen,(mid_sx+4,mid_sy),
                                f"{s.get('trackClass','')} {s.get('speedLimit','')}mph",
                                seg_col)
                # Draw ghost nodes with role labels for turnout
                for i, n in enumerate(nodes):
                    snx, sny = self.unity_to_screen(n['x'], n['z'])
                    if -10 < snx < w+10 and content_top < sny < h:
                        pygame.draw.circle(self.screen, (0,0,0),     (snx,sny), 5)
                        pygame.draw.circle(self.screen, (255,180,0), (snx,sny), 4)
                        pygame.draw.circle(self.screen, (255,255,255),(snx,sny),4, 1)
                        if self.geo_mode == 'turnout' and self.zoom > 0.3:
                            roles = ['entry','through','diverge']
                            lbl = roles[i] if i < len(roles) else str(i)
                            self.font.render_to(self.screen,(snx+6,sny-8),lbl,(200,180,140))

        # ---- Spliney drag preview ----
        if self.dragging_spliney_pt and self.sel_spliney_id:
            sx2, sy2 = self.drag_screen_pos
            pygame.draw.circle(self.screen,(0,0,0),(sx2,sy2),7)
            pygame.draw.circle(self.screen,(0,220,255),(sx2,sy2),6)
            pygame.draw.circle(self.screen,(255,255,255),(sx2,sy2),6,1)
            ux2,uz2 = self.screen_to_unity(sx2,sy2)
            uy2 = self._sample_terrain_y(ux2,uz2) or 0
            self.font.render_to(self.screen,(sx2+10,sy2-8),
                f"{self.sel_spliney_id}[{self.sel_spliney_pt}]  "
                f"({ux2:.1f},{uy2:.1f},{uz2:.1f})",(0,220,255))


    def _draw_measure_guides(self, w, h, content_top):
        start = self._get_track_node_state(getattr(self, 'measure_start_node_id', None))
        end = self._get_track_node_state(getattr(self, 'measure_end_node_id', None))
        origin = self._get_track_node_state(getattr(self, 'station_origin_node_id', None))
        line = self._construction_line()
        y_max = h - STATUS_H

        def draw_marker(node, label, color):
            if not node:
                return
            sx, sy = self.unity_to_screen(node['x'], node['z'])
            if sx < -18 or sx > w + 18 or sy < content_top - 18 or sy > y_max + 18:
                return
            pygame.draw.circle(self.screen, (0, 0, 0), (sx, sy), 8)
            pygame.draw.circle(self.screen, color, (sx, sy), 7)
            pygame.draw.circle(self.screen, (255, 255, 255), (sx, sy), 7, 1)
            self.font.render_to(self.screen, (sx + 10, sy - 9), label, color)

        if start and end:
            sx0, sy0 = self.unity_to_screen(start['x'], start['z'])
            sx1, sy1 = self.unity_to_screen(end['x'], end['z'])
            pygame.draw.line(self.screen, (80, 120, 160), (sx0, sy0), (sx1, sy1), 1)

        if line:
            sx0, sy0 = self.unity_to_screen(line['start']['x'], line['start']['z'])
            sx1, sy1 = self.unity_to_screen(line['end']['x'], line['end']['z'])
            dx = sx1 - sx0
            dy = sy1 - sy0
            seg_len = max(1.0, (dx * dx + dy * dy) ** 0.5)
            ext = max(w, h) * 2
            ex0 = int(sx0 - dx / seg_len * ext)
            ey0 = int(sy0 - dy / seg_len * ext)
            ex1 = int(sx0 + dx / seg_len * ext)
            ey1 = int(sy0 + dy / seg_len * ext)
            pygame.draw.line(self.screen, (0, 200, 255), (ex0, ey0), (ex1, ey1), 1)
            mid_x = int((sx0 + sx1) * 0.5)
            mid_y = int((sy0 + sy1) * 0.5)
            self.font.render_to(
                self.screen,
                (mid_x + 10, mid_y - 10),
                f"Baseline {line['heading']:.1f} deg",
                (0, 200, 255),
            )

        draw_marker(start, 'A', (0, 180, 255))
        draw_marker(end, 'B', (0, 220, 180))
        draw_marker(origin, 'MP0', (255, 190, 0))

    def _draw_cursors(self, w, h, content_top, mx0, my0):
        """Node-place, measure, connect, and drag cursors."""
        self._draw_measure_guides(w, h, content_top)
        map_bottom = self._profile_panel_top() if getattr(self, 'profile_panel', False) else h

        # Scenery placement ghost. The prefab itself is rendered by the game,
        # so show its origin, heading, transform, and model identifier here.
        if (
            getattr(self, 'scenery_place_mode', False)
            and self.mod_project
            and getattr(self, 'scenery_place_model', '')
        ):
            mx0, my0 = pygame.mouse.get_pos()
            over_panel = False
            if getattr(self, 'scenery_panel', False):
                panel_w = min(w - 40, 860)
                panel_h = min(h - content_top - STATUS_H - 20, 560)
                panel_rect = pygame.Rect(
                    (w - panel_w) // 2,
                    content_top + 10,
                    panel_w,
                    panel_h,
                )
                over_panel = panel_rect.collidepoint(mx0, my0)
            if content_top < my0 < map_bottom and not over_panel:
                ux, uz = self.screen_to_unity(mx0, my0)
                uy = self._sample_terrain_y(ux, uz)
                csx, csy = self.unity_to_screen(ux, uz)
                scale = float(getattr(self, 'scenery_place_scale', 1.0))
                marker_r = max(7, min(16, int(7 + scale * 2)))
                diamond = [
                    (csx, csy - marker_r),
                    (csx + marker_r, csy),
                    (csx, csy + marker_r),
                    (csx - marker_r, csy),
                ]
                pygame.draw.polygon(self.screen, (0, 0, 0), diamond)
                pygame.draw.polygon(self.screen, (100, 255, 170), diamond, 3)
                pygame.draw.circle(self.screen, (255, 255, 255), (csx, csy), 3)

                rot_y = float(getattr(self, 'scenery_place_rotY', 0.0))
                angle = math.radians(rot_y)
                tip = (
                    int(csx + math.sin(angle) * 28),
                    int(csy - math.cos(angle) * 28),
                )
                pygame.draw.line(
                    self.screen, (255, 255, 255), (csx, csy), tip, 4
                )
                pygame.draw.line(
                    self.screen, (100, 255, 170), (csx, csy), tip, 2
                )

                lines = [
                    self.scenery_place_model,
                    (
                        f"({ux:.1f}, {uy:.1f}, {uz:.1f})  "
                        f"Y {rot_y:.0f} deg  {scale:.2f}x"
                    ),
                    "[ / ] rotate   Up / Down scale   click to place",
                ]
                for idx, line_text in enumerate(lines):
                    color = (
                        (160, 255, 200)
                        if idx == 0
                        else (225, 200, 255)
                        if idx == 1
                        else (180, 180, 130)
                    )
                    self.font.render_to(
                        self.screen,
                        (csx + marker_r + 7, csy - 14 + idx * 14),
                        line_text,
                        color,
                    )

        # Node place mode cursor
        if getattr(self, '_geo_node_place_mode', False) and self.mod_project:
            mx0, my0 = pygame.mouse.get_pos()
            if content_top < my0 < map_bottom:
                raw_ux, raw_uz = self.screen_to_unity(mx0, my0)
                anchor = self._resolve_measure_anchor()
                ux, uz, _lock_info = self._apply_measure_constraints(raw_ux, raw_uz, anchor=anchor)
                csx, csy = self.unity_to_screen(ux, uz)
                if self.place_y_lock:
                    uy = self.place_y_value
                elif self.place_y_inherit and getattr(self, '_last_placed_y', None) is not None:
                    uy = self._last_placed_y
                else:
                    uy = self._sample_terrain_y(ux, uz) or 0
                pygame.draw.circle(self.screen, (0, 0, 0), (csx, csy), 9)
                pygame.draw.circle(self.screen, (255, 180, 0), (csx, csy), 8)
                pygame.draw.circle(self.screen, (255, 255, 255), (csx, csy), 8, 1)
                pygame.draw.line(self.screen, (255, 180, 0), (csx - 12, csy), (csx + 12, csy), 1)
                pygame.draw.line(self.screen, (255, 180, 0), (csx, csy - 12), (csx, csy + 12), 1)
                lines = [f"({ux:.1f}, {uy:.1f}, {uz:.1f})"]
                lines.extend(self._build_live_measure_hud(anchor, ux, uy, uz))
                lines.append('click to place  Esc to cancel')
                for idx, line_text in enumerate(lines):
                    col = (255, 200, 80) if idx == 0 else ((0, 220, 255) if idx < len(lines) - 1 else (200, 160, 60))
                    self.font.render_to(self.screen, (csx + 12, csy - 8 + idx * 14), line_text, col)

        if getattr(self, '_geo_guide_place_mode', False) and self.mod_project:
            mx0, my0 = pygame.mouse.get_pos()
            if content_top < my0 < map_bottom:
                raw_ux, raw_uz = self.screen_to_unity(mx0, my0)
                anchor = self._resolve_measure_anchor()
                ux, uz, _lock_info = self._apply_measure_constraints(raw_ux, raw_uz, anchor=anchor)
                csx, csy = self.unity_to_screen(ux, uz)
                pygame.draw.circle(self.screen, (0, 0, 0), (csx, csy), 7)
                pygame.draw.circle(self.screen, (0, 220, 180), (csx, csy), 6)
                pygame.draw.circle(self.screen, (255, 255, 255), (csx, csy), 6, 1)
                lines = [f"Guide ({ux:.1f}, {uz:.1f})"]
                lines.append('click to add guide point  Esc to cancel')
                for idx, line_text in enumerate(lines):
                    col = (0, 220, 180) if idx == 0 else (180, 180, 120)
                    self.font.render_to(self.screen, (csx + 12, csy - 8 + idx * 14), line_text, col)

        # Connect mode preview
        if self._connect_from_node and self.mod_project and not self.mod_panel:
            src = self.mod_project.merged_nodes.get(self._connect_from_node)
            if src:
                sx2, sy2 = self.unity_to_screen(src['x'], src['z'])
                mx0, my0 = pygame.mouse.get_pos()
                pygame.draw.line(self.screen, (0, 200, 255), (sx2, sy2), (mx0, my0), 1)
                pygame.draw.circle(self.screen, (0, 200, 255), (sx2, sy2), 6, 2)
                self.font.render_to(
                    self.screen,
                    (mx0 + 10, my0 - 10),
                    f"Ctrl+click node to connect from {self._connect_from_node}",
                    (0, 200, 255),
                )

        # Node drag preview
        if self.dragging_node and self.drag_node_id:
            sx, sy = self.drag_screen_pos
            orig = self.drag_node_origin or {}
            SNAP_PX = 20
            snap_node_id = None
            snap_seg_id = None
            snap_seg_li = None

            if self.mod_project:
                for li, layer in enumerate(self.mod_project.layers):
                    if not layer.visible:
                        continue
                    for nid, node in layer.nodes.items():
                        if nid == self.drag_node_id or node.get('deleted'):
                            continue
                        snx2, sny2 = self.unity_to_screen(node['x'], node['z'])
                        if ((snx2 - sx) ** 2 + (sny2 - sy) ** 2) ** 0.5 < SNAP_PX:
                            snap_node_id = nid
                            pygame.draw.circle(self.screen, (0, 255, 100), (snx2, sny2), SNAP_PX, 2)
                            pygame.draw.circle(self.screen, (255, 255, 255), (snx2, sny2), SNAP_PX // 2)
                            self.font.render_to(self.screen, (snx2 + 14, sny2 - 8), f"Connect -> {nid}", (0, 255, 100))
                            break
                    if snap_node_id:
                        break

                if not snap_node_id:
                    for li, layer in enumerate(self.mod_project.layers):
                        if not layer.visible:
                            continue
                        for pts, col, seg_id in layer.curves:
                            if not pts:
                                continue
                            for pt in pts[::max(1, len(pts) // 12)]:
                                snx2, sny2 = self.unity_to_screen(pt[0], pt[1])
                                if ((snx2 - sx) ** 2 + (sny2 - sy) ** 2) ** 0.5 < SNAP_PX:
                                    snap_seg_id = seg_id
                                    snap_seg_li = li
                                    shift = pygame.key.get_mods() & pygame.KMOD_SHIFT
                                    seg_snap_col = (255, 200, 0) if shift else (0, 255, 160)
                                    screen_pts = [self.unity_to_screen(p[0], p[1]) for p in pts]
                                    for i in range(len(screen_pts) - 1):
                                        pygame.draw.line(self.screen, seg_snap_col, screen_pts[i], screen_pts[i + 1], 3)
                                    action_lbl = f"=> Turnout into {seg_id}" if shift else f"Insert into {seg_id}"
                                    self.font.render_to(self.screen, (sx + 14, sy + 6), action_lbl, seg_snap_col)
                                    if shift:
                                        self.font.render_to(
                                            self.screen,
                                            (sx + 14, sy + 20),
                                            f"  diverge {self.turnout_direction} {self.turnout_diverge_angle} deg  leg {self.turnout_leg_length}m",
                                            (220, 180, 80),
                                        )
                                    break
                            if snap_seg_id:
                                break

            self._drag_snap_node = snap_node_id
            self._drag_snap_seg = (snap_seg_id, snap_seg_li) if snap_seg_id else None

            shift_held = pygame.key.get_mods() & pygame.KMOD_SHIFT
            csx, csy = sx, sy
            if shift_held and orig.get('x') is not None and not snap_node_id and not snap_seg_id:
                osx, osy = self.unity_to_screen(orig['x'], orig['z'])
                dx_scr = abs(sx - osx)
                dy_scr = abs(sy - osy)
                if dx_scr >= dy_scr:
                    csy = osy
                    axis_lbl = 'X axis'
                else:
                    csx = osx
                    axis_lbl = 'Z axis'
                pygame.draw.line(self.screen, (0, 200, 255), (osx, osy), (csx, csy), 2)
                self.font.render_to(self.screen, (csx + 14, csy - 8), f"Constrained: {axis_lbl}", (0, 200, 255))

            preview_ux, preview_uz = self.screen_to_unity(csx, csy)
            if not shift_held and not snap_node_id and not snap_seg_id:
                anchor = {
                    'id': self.drag_node_id,
                    'x': orig.get('x', 0.0),
                    'y': orig.get('y', 0.0),
                    'z': orig.get('z', 0.0),
                    'rotY': orig.get('rotY', 0.0),
                    'source': 'drag',
                }
                preview_ux, preview_uz, _lock_info = self._apply_measure_constraints(
                    preview_ux, preview_uz, anchor=anchor)
                csx, csy = self.unity_to_screen(preview_ux, preview_uz)

            ghost_col = (0, 255, 100) if snap_node_id else (
                (255, 200, 0) if (snap_seg_id and shift_held) else
                ((0, 255, 160) if snap_seg_id else (255, 200, 0))
            )
            pygame.draw.circle(self.screen, (0, 0, 0), (csx, csy), 9)
            pygame.draw.circle(self.screen, ghost_col, (csx, csy), 8)
            pygame.draw.circle(self.screen, (255, 255, 255), (csx, csy), 8, 1)
            if orig.get('x') is not None:
                osx, osy = self.unity_to_screen(orig['x'], orig['z'])
                pygame.draw.line(self.screen, ghost_col, (osx, osy), (csx, csy), 1)
            if not snap_node_id and not snap_seg_id:
                uy = self._sample_terrain_y(preview_ux, preview_uz) or orig.get('y', 0)
                hud_anchor = {
                    'id': self.drag_node_id,
                    'x': orig.get('x', 0.0),
                    'y': orig.get('y', 0.0),
                    'z': orig.get('z', 0.0),
                    'rotY': orig.get('rotY', 0.0),
                    'source': 'drag',
                }
                lines = [f"{self.drag_node_id}  ({preview_ux:.1f}, {uy:.1f}, {preview_uz:.1f})"]
                lines.extend(self._build_live_measure_hud(hud_anchor, preview_ux, uy, preview_uz))
                for idx, line_text in enumerate(lines):
                    col = (255, 220, 80) if idx == 0 else (0, 220, 255)
                    self.font.render_to(self.screen, (csx + 12, csy - 8 + idx * 14), line_text, col)

    def _draw_hover_info(self, w, h, content_top, mx0, my0):
        """Hover tooltip / node+segment info."""
        # ---- Selection overlay ----
        # Lasso in-progress: draw the polygon outline
        if self.sel_dragging and self.sel_tool == 'lasso' and len(self.sel_lasso_pts) >= 2:
            pts_screen = self.sel_lasso_pts
            for i in range(len(pts_screen) - 1):
                pygame.draw.line(self.screen, (80, 220, 120),
                                 pts_screen[i], pts_screen[i+1], 2)
            # Close back to start
            pygame.draw.line(self.screen, (80, 220, 120),
                             pts_screen[-1], pts_screen[0], 1)

        # Rect drag in-progress
        elif self.sel_dragging and self.sel_tool == 'rect' and self.sel_drag_start and self.sel_drag_end:
            r0 = min(self.sel_drag_start[0], self.sel_drag_end[0])
            r1 = max(self.sel_drag_start[0], self.sel_drag_end[0])
            c0 = min(self.sel_drag_start[1], self.sel_drag_end[1])
            c1 = max(self.sel_drag_start[1], self.sel_drag_end[1])
            sx0, sy0 = self.wp_to_screen(r0, c0)
            sx1, sy1 = self.wp_to_screen(r1 + 1, c1 + 1)
            sel_rect = pygame.Rect(int(sx0), int(sy0),
                                   max(1, int(sx1-sx0)), max(1, int(sy1-sy0)))
            drag_surf = pygame.Surface((sel_rect.w, sel_rect.h), pygame.SRCALPHA)
            drag_surf.fill((80, 160, 255, 25))
            self.screen.blit(drag_surf, sel_rect.topleft)
            pygame.draw.rect(self.screen, (80, 180, 255), sel_rect, 2)

        # Committed selection (rect or lasso with mask)
        if self.selection is not None and not self.sel_dragging:
            sel = self.selection
            sx0, sy0 = self.wp_to_screen(sel.r0, sel.c0)
            sx1, sy1 = self.wp_to_screen(sel.r1 + 1, sel.c1 + 1)
            pw_s = max(1, int(sx1 - sx0)); ph_s = max(1, int(sy1 - sy0))

            # Tinted fill
            fill_s = pygame.Surface((pw_s, ph_s), pygame.SRCALPHA)
            fill_s.fill((80, 160, 255, 22))
            self.screen.blit(fill_s, (int(sx0), int(sy0)))

            # Border
            pygame.draw.rect(self.screen, (80, 180, 255),
                             (int(sx0), int(sy0), pw_s, ph_s), 2)

            # Corner handles
            for cx3, cy3 in [(int(sx0), int(sy0)), (int(sx1), int(sy0)),
                              (int(sx0), int(sy1)), (int(sx1), int(sy1))]:
                pygame.draw.circle(self.screen, (255,255,255), (cx3, cy3), 4)
                pygame.draw.circle(self.screen, (80,160,255),  (cx3, cy3), 4, 1)

            # Label
            sel_pct = int(sel.mask.sum() * 100 / max(1, sel.h * sel.w))
            lbl = f"{sel.h}x{sel.w}  {sel_pct}%"
            self.font.render_to(self.screen, (int(sx0)+4, int(sy0)+4), lbl, (180,220,255))

        # Paste preview
        if self.sel_pending_paste and self.clipboard is not None:
            mx_p, my_p = pygame.mouse.get_pos()
            if my_p > content_top:
                wr_p, wc_p = self.screen_to_wp(mx_p, my_p)
                sx_p, sy_p = self.wp_to_screen(wr_p, wc_p)
                px_sz = (self.tile_size * self.zoom) / TILE_STRIDE
                pw2 = max(1, int(self.clipboard.w * px_sz))
                ph2 = max(1, int(self.clipboard.h * px_sz))
                paste_surf = pygame.Surface((pw2, ph2), pygame.SRCALPHA)
                paste_surf.fill((180, 100, 255, 40))
                self.screen.blit(paste_surf, (int(sx_p), int(sy_p)))
                pygame.draw.rect(self.screen, (180, 100, 255),
                                 (int(sx_p), int(sy_p), pw2, ph2), 2)




    def _draw_welcome(self, w, h, content_top):
        """Welcome screen when no tiles loaded."""
        self._welcome_action_rects = []
        if self.tiles or getattr(self, '_welcome_dismissed', False):
            return

        card_w = min(760, max(420, w - 96))
        card_h = 232
        card_x = max(24, (w - card_w) // 2)
        card_y = content_top + max(24, (h - content_top - STATUS_H - card_h) // 2)
        card = pygame.Rect(card_x, card_y, card_w, card_h)

        panel = pygame.Surface((card.width, card.height), pygame.SRCALPHA)
        panel.fill((10, 16, 26, 228))
        self.screen.blit(panel, card.topleft)
        pygame.draw.rect(self.screen, (32, 46, 66), card, 1, border_radius=14)
        pygame.draw.rect(self.screen, ACCENT_COLOR, (card.x, card.y, 5, card.height), border_radius=3)

        # X close button (top-right corner)
        mouse_pos = pygame.mouse.get_pos()
        xbtn = pygame.Rect(card.right - 28, card.y + 8, 20, 20)
        xhov = xbtn.collidepoint(*mouse_pos)
        pygame.draw.rect(self.screen, (180, 60, 60) if xhov else (60, 30, 30), xbtn, border_radius=3)
        self.font_big.render_to(self.screen, (card.right - 22, card.y + 11), "X", (220, 180, 180))
        self._welcome_action_rects.append((xbtn, 'dismiss_welcome'))

        cx = card.x + 28
        cy = card.y + 24
        self.font_big.render_to(self.screen, (cx, cy), "Railroader Terrain & Mod Editor", ACCENT_COLOR)
        cy += 26
        if self.mod_project:
            self.font.render_to(
                self.screen,
                (cx, cy),
                f"Project loaded: {self.mod_project.name}. Load tiles or open project tools from the top bar.",
                TEXT_COLOR,
            )
        else:
            self.font.render_to(
                self.screen,
                (cx, cy),
                "Load terrain tiles, open a mod project, or load a graph to start editing.",
                TEXT_COLOR,
            )
        cy += 22
        self.font.render_to(
            self.screen,
            (cx, cy),
            "The main workflow is back in the top bar so the canvas stays open and readable.",
            DIM_COLOR,
        )
        cy += 26

        for line in [
            "Load Tiles to edit terrain data or drag a tile folder onto the window.",
            "Open Mod to browse towns, industries, progression files, and graph layers.",
            "Load Graph if you only want track geometry without a full mod project.",
        ]:
            self.font.render_to(self.screen, (cx, cy), "-", ACCENT_COLOR)
            self.font.render_to(self.screen, (cx + 16, cy), line, DIM_COLOR)
            cy += 18

        btn_y = card.bottom - 52
        btn_specs = [
            ("Open Mod", "open_mod", (0, 160, 220)),
            ("Load Tiles", "load_tiles", (80, 180, 255)),
            ("Load Graph", "load_graph", (120, 160, 255)),
        ]
        btn_widths = [self.font_big.get_rect(label).width + 24 for label, _, _ in btn_specs]
        total_btn_w = sum(btn_widths) + 10 * (len(btn_specs) - 1)
        bx = card.x + (card.width - total_btn_w) // 2
        for (label, action, color), bw in zip(btn_specs, btn_widths):
            rect = pygame.Rect(bx, btn_y, bw, 30)
            hover = rect.collidepoint(*mouse_pos)
            self._draw_button(self.screen, rect, label, True, hover, color)
            self._welcome_action_rects.append((rect, action))
            bx += bw + 10


    def _draw_navbar(self, w, h, content_top, mx0, my0):
        """Two-row top bar for primary workflow and editor panels."""
        self._shell_action_rects = []
        self._shell_sidebar_rects = []
        self._shell_sidebar_bounds = None

        tile_values = list(self.tiles.values())
        dirty_tiles = sum(1 for t in tile_values if t.dirty)
        dirty_project = bool(self.mod_project and self.mod_project.dirty)
        dirty_progression = bool(self.prog_project and self.prog_project.dirty)
        dirty_towns = bool(self._area_dirty_layers)
        dirty_count = dirty_tiles + int(dirty_project) + int(dirty_progression) + int(dirty_towns)
        apply_live = bool(getattr(self, 'live_mod_apply', True))
        pending_apply = int(self._pending_mod_apply_count()) if hasattr(self, '_pending_mod_apply_count') else 0
        reload_ready = self._has_reloadable_source()
        reload_warn = self._has_unsaved_reload_changes()
        track_data_loaded = bool(self.track_segments) or bool(self.mod_project and self.mod_project.merged_segments)
        measure_enabled = bool((self.mod_project and self.mod_project.merged_nodes) or self.track_node_list)
        profile_enabled = track_data_loaded or measure_enabled

        header = pygame.Surface((w, PANEL_H), pygame.SRCALPHA)
        header.fill((13, 18, 28, 244))
        self.screen.blit(header, (0, 0))
        pygame.draw.line(self.screen, (24, 34, 50), (0, 37), (w, 37), 1)
        pygame.draw.line(self.screen, BORDER_COLOR, (0, PANEL_H), (w, PANEL_H), 1)

        def draw_top_button(label, action, x, y, *, state='neutral', color=ACCENT_COLOR, enabled=True):
            bw = self.font_big.get_rect(label).width + 18
            rect = pygame.Rect(x, y, bw, 24)
            hover = rect.collidepoint(mx0, my0)

            if not enabled:
                fill = (22, 28, 38)
                border = (44, 52, 64)
                text_col = (92, 102, 114)
            elif state == 'active':
                fill = tuple(min(255, 18 + int(c * 0.28)) for c in color)
                border = color
                text_col = color
            elif state == 'accent':
                fill = tuple(min(255, c + 8) for c in BTN_HOVER_C) if hover else BTN_INACTIVE
                border = color
                text_col = TEXT_COLOR if hover else color
            elif state == 'warning':
                fill = (72, 56, 24) if hover else (52, 42, 24)
                border = WARN_COLOR
                text_col = TEXT_COLOR if hover else WARN_COLOR
            else:
                fill = BTN_HOVER_C if hover else BTN_INACTIVE
                border = BTN_BORDER
                text_col = TEXT_COLOR if hover else TEXT_SOFT

            pygame.draw.rect(self.screen, fill, rect, border_radius=5)
            pygame.draw.rect(self.screen, border, rect, 1, border_radius=5)
            if enabled and state in ('active', 'accent', 'warning'):
                pygame.draw.rect(self.screen, border, (rect.x, rect.bottom - 3, rect.width, 3), border_radius=2)

            tr, _ = self.font_big.render(label, text_col)
            self.screen.blit(
                tr,
                (rect.x + (rect.width - tr.get_width()) // 2,
                 rect.y + (rect.height - tr.get_height()) // 2),
            )
            if enabled:
                self._shell_action_rects.append((rect, action))
            return rect

        def draw_status_chip(text, x, y, color=TEXT_SOFT):
            bw = self.font.get_rect(text).width + 14
            rect = pygame.Rect(x, y + 2, bw, 20)
            pygame.draw.rect(self.screen, PANEL_SECTION_BG, rect, border_radius=10)
            pygame.draw.rect(
                self.screen,
                color if color != TEXT_SOFT else PANEL_SECTION_BORDER,
                rect,
                1,
                border_radius=10,
            )
            self.font.render_to(self.screen, (rect.x + 7, rect.y + 3), text, color)
            return rect

        row1_y = 7
        row2_y = 45
        x = 8
        for label, action, active, color, enabled in [
            ("Heightmap", "mode_height", self.mode == 'height', MODE_COLORS['height'], True),
            ("Vegetation", "mode_veg", self.mode == 'veg', MODE_COLORS['veg'], True),
            ("Water", "mode_water", self.mode == 'water', MODE_COLORS['water'], True),
            ("Hillshade", "toggle_hillshade", self.hillshade, ACCENT_COLOR, bool(self.tiles)),
            ("Fit", "fit_view", False, ACCENT_COLOR, bool(self.tiles)),
            ("Tracks", "toggle_tracks", self.show_tracks, WARN_COLOR, track_data_loaded),
            ("Nodes", "toggle_nodes", self.show_nodes, WARN_COLOR, track_data_loaded),
            ("Elev Color", "toggle_elev_colors", self.show_elev_colors, (80, 200, 255), track_data_loaded),
            ("Grades", "toggle_grade_labels", self.show_grade_labels, (0, 220, 140), track_data_loaded),
        ]:
            state = 'active' if active else 'neutral'
            rect = draw_top_button(label, action, x, row1_y, state=state, color=color, enabled=enabled)
            x = rect.right + 6

        quick_specs = [
            ("Open Mod", "open_mod", 'accent', (0, 160, 220), True),
            ("Load Graph", "load_graph", 'accent', (120, 160, 255), True),
            ("Load Tiles", "load_tiles", 'accent', (80, 180, 255), True),
            ("Reload", "reload_sources", 'warning' if reload_warn else ('accent' if reload_ready else 'neutral'),
             WARN_COLOR if reload_warn else ACCENT_COLOR, reload_ready),
        ]
        osm_control_specs = []
        osm_controls_w = 0
        if self.osm.enabled:
            cache_ready, cache_files, cache_bytes = self.osm.cache_stats()
            if not cache_ready:
                cache_label = "checking..."
            elif cache_bytes >= 1024 * 1024 * 1024:
                cache_label = f"{cache_bytes / (1024**3):.2f} GB"
            elif cache_bytes >= 1024 * 1024:
                cache_label = f"{cache_bytes / (1024**2):.0f} MB"
            elif cache_bytes >= 1024:
                cache_label = f"{cache_bytes / 1024:.0f} KB"
            else:
                cache_label = f"{cache_bytes} B"
            osm_control_specs = [
                ("Zoom -", "osm_zoom_out", OK_COLOR),
                ("Zoom +", "osm_zoom_in", OK_COLOR),
                ("Fade -", "osm_opacity_down", ACCENT_COLOR),
                ("Fade +", "osm_opacity_up", ACCENT_COLOR),
                (
                    "CONFIRM CLEAR" if self.osm_clear_cache_confirm else "Clear Cache",
                    "osm_clear_cache",
                    WARN_COLOR,
                ),
            ]
            osm_pct = int(round(self.osm.opacity * 100 / 255))
            osm_chip_text = (
                f"OSM z{self.osm.zoom}  {osm_pct}%  •  "
                f"{cache_label} / {cache_files:,} tiles"
            )
            osm_chip_w = self.font.get_rect(osm_chip_text).width + 14
            osm_controls_w = osm_chip_w + 8
            osm_controls_w += sum(self.font_big.get_rect(label).width + 18 for label, _, _ in osm_control_specs)
            osm_controls_w += 6 * len(osm_control_specs)
        quick_width = sum(self.font_big.get_rect(label).width + 18 for label, *_ in quick_specs) + 6 * (len(quick_specs) - 1)
        if self.osm.enabled and x + 16 + osm_controls_w + quick_width > w - 10:
            osm_control_specs = [
                ("Z-", "osm_zoom_out", OK_COLOR),
                ("Z+", "osm_zoom_in", OK_COLOR),
                ("Op-", "osm_opacity_down", ACCENT_COLOR),
                ("Op+", "osm_opacity_up", ACCENT_COLOR),
                (
                    "CONFIRM" if self.osm_clear_cache_confirm else "Clear",
                    "osm_clear_cache",
                    WARN_COLOR,
                ),
            ]
            osm_pct = int(round(self.osm.opacity * 100 / 255))
            osm_chip_text = (
                f"OSM z{self.osm.zoom} {osm_pct}% • {cache_label}"
            )
            osm_chip_w = self.font.get_rect(osm_chip_text).width + 14
            osm_controls_w = osm_chip_w + 8
            osm_controls_w += sum(self.font_big.get_rect(label).width + 18 for label, _, _ in osm_control_specs)
            osm_controls_w += 6 * len(osm_control_specs)
        quick_x = max(x + 16 + osm_controls_w, w - quick_width - 10)
        if self.osm.enabled:
            osm_x = max(x + 16, quick_x - osm_controls_w)
            osm_pct = int(round(self.osm.opacity * 100 / 255))
            chip = draw_status_chip(
                osm_chip_text, osm_x, row1_y, OK_COLOR)
            osm_x = chip.right + 8
            for label, action, color in osm_control_specs:
                rect = draw_top_button(label, action, osm_x, row1_y, state='neutral', color=color, enabled=True)
                osm_x = rect.right + 6
        for label, action, state, color, enabled in quick_specs:
            rect = draw_top_button(label, action, quick_x, row1_y, state=state, color=color, enabled=enabled)
            quick_x = rect.right + 6

        x = 8
        panel_specs = [
            ("Generate", "toggle_generate", self.gen_panel, OK_COLOR, True),
            ("Tile Cleanup", "toggle_tile_cleanup", self.tile_delete_mode, WARN_COLOR, bool(self.tiles)),
            ("Mod", "toggle_mod", self.mod_panel, (160, 100, 255), True),
            ("Towns", "open_areas", self.area_panel, (100, 200, 140), bool(self.mod_project)),
            ("Progress", "open_progression", self.prog_panel, (255, 140, 0), bool(self.mod_project)),
            ("Spans", "toggle_spans", self.span_panel, (100, 180, 255), bool(self.mod_project)),
            ("Geo", "toggle_geo", self.geo_panel, (255, 80, 160), bool(self.mod_project)),
            ("Measure", "toggle_measure", self.calc_panel and self.calc_mode == 'measure', (0, 212, 255), measure_enabled),
            ("Profile", "toggle_profile", self.profile_panel, (80, 180, 255), profile_enabled),
            ("Mandela", "toggle_mandela", self.mandela_panel, (200, 140, 255), bool(self.mod_project)),
            ("Scenery", "toggle_scenery", self.scenery_panel, (200, 130, 255), bool(self.mod_project)),
            ("OSM", "toggle_osm", self.osm.enabled, OK_COLOR, True),
            ("Diff", "toggle_diff", self.diff_mode, WARN_COLOR, bool(self.tiles)),
            ("Save", "save_all", bool(dirty_count), WARN_COLOR, bool(dirty_count)),
            ("Auto Save", "apply_mode_live", apply_live, OK_COLOR, True),
            ("Manual", "apply_mode_batch", not apply_live, WARN_COLOR, True),
            ("Help", "toggle_help", self.show_help, DIM_COLOR, True),
            ("Edit", "toggle_edit", self.edit_mode, ACCENT2_COLOR, True),
        ]
        for label, action, active, color, enabled in panel_specs:
            state = 'warning' if action == 'save_all' and dirty_count else ('active' if active else 'neutral')
            rect = draw_top_button(label, action, x, row2_y, state=state, color=color, enabled=enabled)
            x = rect.right + 6

        status_right = w - 10
        if _BRIDGE_AVAILABLE:
            bridge_text = "Bridge Live" if self.bridge_connected else "Bridge Off"
            bridge_color = OK_COLOR if self.bridge_connected else DIM_COLOR
            bridge_w = self.font.get_rect(bridge_text).width + 14
            bridge_x = status_right - bridge_w
            draw_status_chip(bridge_text, bridge_x, row2_y, bridge_color)
            status_right = bridge_x - 10

        apply_text = "Auto Save" if apply_live else (f"Manual {pending_apply}" if pending_apply else "Manual")
        apply_color = OK_COLOR if apply_live else (WARN_COLOR if pending_apply else DIM_COLOR)
        apply_w = self.font.get_rect(apply_text).width + 14
        apply_x = status_right - apply_w
        draw_status_chip(apply_text, apply_x, row2_y, apply_color)
        status_right = apply_x - 10

        status_candidates = []
        if self.mod_project:
            status_candidates.append(f"{self.mod_project.name} | Tiles {len(tile_values)} | Dirty {dirty_count} | Zoom {self.zoom:.1f}x")
        status_candidates.append(f"Tiles {len(tile_values)} | Dirty {dirty_count} | Zoom {self.zoom:.1f}x")
        status_candidates.append(f"Dirty {dirty_count} | Zoom {self.zoom:.1f}x")
        status_color = WARN_COLOR if dirty_count else DIM_COLOR
        status_min_x = x + 12
        for status_text in status_candidates:
            status_rect = self.font.get_rect(status_text)
            status_x = status_right - status_rect.width
            if status_x >= status_min_x:
                self.font.render_to(self.screen, (status_x, row2_y + 5), status_text, status_color)
                break


    def _draw_tile_cleanup_panel(self, w, h, content_top, mx0, my0):
        """Compact controls for selecting and recoverably deleting map tiles."""
        self._tile_cleanup_rects = []
        if not getattr(self, 'tile_delete_mode', False):
            return

        panel_w = min(max(760, w - 40), 1100)
        panel_h = 104
        panel_x = max(10, (w - panel_w) // 2)
        panel_y = content_top + 10
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        self._tile_cleanup_panel_rect = panel_rect
        panel = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        panel.fill((8, 12, 19, 242))
        self.screen.blit(panel, panel_rect.topleft)
        pygame.draw.rect(
            self.screen, (226, 73, 65), panel_rect, 2, border_radius=8
        )

        selected_count = len(
            set(getattr(self, 'tile_delete_selection', set())) & set(self.tiles)
        )
        title = f"TILE CLEANUP  |  {selected_count} MARKED FOR DELETION"
        self.font_big.render_to(
            self.screen, (panel_x + 12, panel_y + 8), title,
            (255, 112, 96) if selected_count else TEXT_COLOR,
        )
        hint = (
            "Drag = replace selection   Shift+drag = add   "
            "Ctrl+drag or right-drag = keep/remove   MMB = pan"
        )
        self.font.render_to(
            self.screen, (panel_x + 12, panel_y + 29), hint, TEXT_SOFT
        )

        button_y = panel_y + 48
        gap = 7
        specs = [
            ("EXIT", 'cleanup_exit', 62, ACCENT_COLOR, True),
            ("CLEAR", 'cleanup_clear', 72, DIM_COLOR, selected_count > 0),
            ("SELECT ALL", 'cleanup_all', 104, WARN_COLOR, bool(self.tiles)),
            ("INVERT / OUTSIDE ROW", 'cleanup_invert', 174, OK_COLOR, bool(self.tiles)),
        ]
        delete_label = (
            f"CONFIRM MOVE {selected_count} TO RECOVERY"
            if getattr(self, 'tile_delete_confirm', False)
            else f"DELETE {selected_count} MARKED TILES"
        )
        fixed_width = sum(item[2] for item in specs) + gap * len(specs)
        delete_width = max(220, panel_w - 24 - fixed_width)
        specs.append((
            delete_label, 'cleanup_delete', delete_width, (255, 76, 62),
            selected_count > 0,
        ))

        bx = panel_x + 12
        for label, action, bw, color, enabled in specs:
            rect = pygame.Rect(bx, button_y, bw, 29)
            hover = enabled and rect.collidepoint(mx0, my0)
            if enabled:
                fill = BTN_HOVER_C if hover else BTN_INACTIVE
                border = color
                text_color = TEXT_COLOR if hover else color
            else:
                fill = (19, 24, 31)
                border = (47, 54, 64)
                text_color = (83, 91, 102)
            pygame.draw.rect(self.screen, fill, rect, border_radius=5)
            pygame.draw.rect(self.screen, border, rect, 1, border_radius=5)
            text_surf, _ = self.font.render(label, text_color)
            self.screen.blit(
                text_surf,
                (rect.x + (rect.width - text_surf.get_width()) // 2,
                 rect.y + (rect.height - text_surf.get_height()) // 2),
            )
            if enabled:
                self._tile_cleanup_rects.append((rect, action))
            bx = rect.right + gap

        footer = (
            "ROW shortcut: mark tiles along the right-of-way, then click "
            "INVERT / OUTSIDE ROW. Deleted files remain recoverable; Ctrl+Z restores."
        )
        self.font.render_to(
            self.screen, (panel_x + 12, panel_y + 84), footer, DIM_COLOR
        )

    def _draw_toolbar(self, w, h, content_top, mx0, my0):
        """Edit-mode toolbar: brush controls, selection tools, strength/size."""
        # ================================================================
        # TOOLBAR (edit mode only)
        # ================================================================
        if self.edit_mode:
            tby = PANEL_H
            pygame.draw.rect(self.screen, TOOLBAR_COLOR, (0, tby, w, TOOLBAR_H))
            pygame.draw.line(self.screen, BORDER_COLOR, (0, tby + TOOLBAR_H), (w, tby + TOOLBAR_H), 1)

            tbx = 10
            tby_mid = tby + (TOOLBAR_H - 28) // 2

            # Selection tool toggle ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â always visible in edit mode
            sel_bw = self.font_big.get_rect("Select").width + 14
            hover_sel = pygame.Rect(tbx, tby_mid, sel_bw, 28).collidepoint(mx0, my0)
            self._draw_button(self.screen, (tbx, tby_mid, sel_bw, 28), "Select",
                              self.select_mode, hover_sel, (80, 180, 255))
            tbx += sel_bw + 4

            if self.select_mode:
                # Selection tool type sub-buttons
                tool_colors = {'rect': ACCENT_COLOR, 'lasso': OK_COLOR, 'wand': WARN_COLOR}
                tool_labels = {'rect': 'Rect', 'lasso': 'Lasso', 'wand': 'Wand'}
                for st in ['rect', 'lasso', 'wand']:
                    stbw = self.font_big.get_rect(tool_labels[st]).width + 12
                    sthov = pygame.Rect(tbx, tby_mid, stbw, 28).collidepoint(mx0, my0)
                    self._draw_button(self.screen, (tbx, tby_mid, stbw, 28),
                                      tool_labels[st], self.sel_tool == st,
                                      sthov, tool_colors[st])
                    tbx += stbw + 4

                # Wand tolerance
                if self.sel_tool == 'wand':
                    self._draw_separator(self.screen, tbx+3, tby_mid, 28); tbx += 12
                    wt_lbl = f"Tol {self.sel_wand_tol}"
                    self.font_big.render_to(self.screen, (tbx, tby_mid+6), wt_lbl, TEXT_COLOR)
                    tbx += self.font_big.get_rect(wt_lbl).width + 6
                    for sym_w in ('-', '+'):
                        brw = pygame.Rect(tbx, tby_mid+2, 22, 24)
                        hov_w = brw.collidepoint(mx0, my0)
                        pygame.draw.rect(self.screen, BTN_HOVER_C if hov_w else BTN_INACTIVE, brw, border_radius=4)
                        pygame.draw.rect(self.screen, BTN_BORDER, brw, 1, border_radius=4)
                        self.font_big.render_to(self.screen, (tbx+5, tby_mid+5), sym_w, TEXT_COLOR)
                        tbx += 26

                self._draw_separator(self.screen, tbx+3, tby_mid, 28); tbx += 12

                # Selection operation buttons
                has_sel = self.selection is not None
                has_cb  = self.clipboard is not None
                for lbl4, active4, col4 in [
                    ("Copy",     has_sel, ACCENT_COLOR),
                    ("Cut",      has_sel, WARN_COLOR),
                    ("Paste",    has_cb,  (180,100,255)),
                    ("Fill",     has_sel, OK_COLOR),
                    ("Flip H",   has_sel, DIM_COLOR),
                    ("Flip V",   has_sel, DIM_COLOR),
                    ("Rot 90",   has_sel, DIM_COLOR),
                    ("Deselect", has_sel, (100,100,120)),
                ]:
                    bw4 = self.font_big.get_rect(lbl4).width + 12
                    hov4 = pygame.Rect(tbx, tby_mid, bw4, 28).collidepoint(mx0, my0)
                    self._draw_button(self.screen, (tbx, tby_mid, bw4, 28),
                                      lbl4, False, hov4, col4 if active4 else None)
                    tbx += bw4 + 4

                if self.selection:
                    self._draw_separator(self.screen, tbx+4, tby_mid, 28); tbx += 12
                    sel_pct = int(self.selection.mask.sum() * 100 /
                                  max(1, self.selection.h * self.selection.w))
                    info = f"{self.selection.h}x{self.selection.w}  {sel_pct}% filled"
                    self.font.render_to(self.screen, (tbx, tby_mid+8), info, DIM_COLOR)
            else:
                self._draw_separator(self.screen, tbx + 4, tby_mid, 28); tbx += 14

            # Brush mode buttons (height only) ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â now includes Erode
            if self.mode == 'height':
                bm_colors = {'raise': ACCENT_COLOR, 'flatten': WARN_COLOR,
                             'paint': (180, 100, 255), 'smooth': OK_COLOR,
                             'noise': (255, 160, 50), 'erode': (200, 100, 60)}
                bm_labels = {'raise': 'Raise', 'flatten': 'Flatten',
                             'paint': 'Paint', 'smooth': 'Smooth',
                             'noise': 'Noise', 'erode': 'Erode'}
                for bm in ['raise', 'flatten', 'paint', 'smooth', 'noise', 'erode']:
                    lbl   = bm_labels[bm]
                    bm_bw = self.font_big.get_rect(lbl).width + 14
                    hover  = pygame.Rect(tbx, tby_mid, bm_bw, 28).collidepoint(mx0, my0)
                    self._draw_button(self.screen, (tbx, tby_mid, bm_bw, 28), lbl,
                                      self.brush_mode == bm, hover, bm_colors[bm])
                    tbx += bm_bw + 4
                self._draw_separator(self.screen, tbx + 4, tby_mid, 28); tbx += 16

            # Brush size
            sz_label = f"Size  {self.brush_radius}px"
            self.font_big.render_to(self.screen, (tbx, tby_mid + 6), sz_label, TEXT_COLOR)
            tbx += self.font_big.get_rect(sz_label).width + 8
            # [-] [+] buttons
            for sym, delta in [('-', -4), ('+', 4)]:
                btn_r = pygame.Rect(tbx, tby_mid + 2, 24, 24)
                hover  = btn_r.collidepoint(mx0, my0)
                pygame.draw.rect(self.screen, BTN_HOVER_C if hover else BTN_INACTIVE, btn_r, border_radius=4)
                pygame.draw.rect(self.screen, BTN_BORDER, btn_r, 1, border_radius=4)
                self.font_big.render_to(self.screen, (tbx + 6, tby_mid + 5), sym, TEXT_COLOR)
                tbx += 28

            self._draw_separator(self.screen, tbx + 4, tby_mid, 28); tbx += 16

            if self.mode == 'height':
                # Strength
                st_label = f"Strength  {self.brush_strength:.3f}"
                self.font_big.render_to(self.screen, (tbx, tby_mid + 6), st_label, TEXT_COLOR)
                tbx += self.font_big.get_rect(st_label).width + 8
                for sym, delta in [('-', -1), ('+', 1)]:
                    btn_r = pygame.Rect(tbx, tby_mid + 2, 24, 24)
                    hover  = btn_r.collidepoint(mx0, my0)
                    pygame.draw.rect(self.screen, BTN_HOVER_C if hover else BTN_INACTIVE, btn_r, border_radius=4)
                    pygame.draw.rect(self.screen, BTN_BORDER, btn_r, 1, border_radius=4)
                    self.font_big.render_to(self.screen, (tbx + 6, tby_mid + 5), sym, TEXT_COLOR)
                    tbx += 28
                self._draw_separator(self.screen, tbx + 4, tby_mid, 28); tbx += 16

                # Clamps
                floor_str = f"Floor: {self.clamp_floor_m:.0f}m" if self.clamp_floor_m is not None else "Floor: off"
                ceil_str  = f"Ceil: {self.clamp_ceil_m:.0f}m"   if self.clamp_ceil_m  is not None else "Ceil: off"
                fc = WARN_COLOR if self.clamp_floor_m is not None else DIM_COLOR
                cc2 = WARN_COLOR if self.clamp_ceil_m  is not None else DIM_COLOR
                self.font_big.render_to(self.screen, (tbx, tby_mid + 1), floor_str, fc)
                tbx += self.font_big.get_rect(floor_str).width + 12
                self.font_big.render_to(self.screen, (tbx, tby_mid + 1), ceil_str, cc2)
                tbx += self.font_big.get_rect(ceil_str).width + 12

                # Paint target (paint mode)
                if self.brush_mode == 'paint':
                    tgt_str = (f"Target: {self._h16_to_m(self.paint_target):.1f}m"
                               if self.paint_target is not None else "Target: MMB to set")
                    self.font_big.render_to(self.screen, (tbx, tby_mid + 1), tgt_str,
                                            (180, 100, 255) if self.paint_target is not None else DIM_COLOR)

                # Noise scale (noise mode)
                if self.brush_mode == 'noise':
                    ns_label = f"Scale  {self.noise_scale}px"
                    self.font_big.render_to(self.screen, (tbx, tby_mid + 6), ns_label, TEXT_COLOR)
                    tbx += self.font_big.get_rect(ns_label).width + 8
                    for sym3 in [('-', -8), ('+', 8)]:
                        btn_r3 = pygame.Rect(tbx, tby_mid + 2, 24, 24)
                        hov3   = btn_r3.collidepoint(mx0, my0)
                        pygame.draw.rect(self.screen, BTN_HOVER_C if hov3 else BTN_INACTIVE, btn_r3, border_radius=4)
                        pygame.draw.rect(self.screen, BTN_BORDER, btn_r3, 1, border_radius=4)
                        self.font_big.render_to(self.screen, (tbx + 6, tby_mid + 5), sym3[0], TEXT_COLOR)
                        tbx += 28

            elif self.mode == 'veg':
                self.font_big.render_to(self.screen, (tbx, tby_mid + 6), "Preset:", TEXT_COLOR)
                tbx += self.font_big.get_rect("Preset:").width + 8
                for i in range(8):
                    lbl   = str(i)
                    vbw   = 26
                    active = i == self.veg_preset
                    hover  = pygame.Rect(tbx, tby_mid + 2, vbw, 24).collidepoint(mx0, my0)
                    col   = pygame.Color(*VEG_COLORS[i])
                    self._draw_button(self.screen, (tbx, tby_mid + 2, vbw, 24), lbl, active, hover, col)
                    tbx += vbw + 3
                tbx += 8
                self.font_big.render_to(self.screen, (tbx, tby_mid + 6),
                    VEG_NAMES[self.veg_preset], pygame.Color(*VEG_COLORS[self.veg_preset]))
                description_x = tbx + self.font_big.get_rect(
                    VEG_NAMES[self.veg_preset]).width + 10
                self.font.render_to(
                    self.screen,
                    (description_x, tby_mid + 8),
                    VEG_DESCRIPTIONS[self.veg_preset],
                    TEXT_SOFT)

            elif self.mode == 'water':
                self.font_big.render_to(self.screen, (tbx, tby_mid + 6),
                    "LMB  Add water    RMB  Remove water    MMB  Sample", DIM_COLOR)

            # Right side of toolbar: live cursor readout
            if self.cursor_height_m is not None:
                parts = [f"{self.cursor_height_m:.1f} m"]
                if self.cursor_veg is not None:
                    parts.append(f"Veg {self.cursor_veg}: {VEG_NAMES[self.cursor_veg]}")
                if self.cursor_water:
                    parts.append("Water")
                readout = "   |   ".join(parts)
                rr2, _ = self.font_big.render(readout, ACCENT_COLOR)
                self.screen.blit(rr2, (w - rr2.get_width() - 16,
                                       tby + (TOOLBAR_H - rr2.get_height()) // 2))


    def _draw_status_and_overlays(self, w, h, content_top, mx0, my0):
        """Brush cursor, status bar, loading overlay, tile tooltip, generate panel, help."""
        # ================================================================
        # BRUSH CURSOR
        # ================================================================
        if self.edit_mode and my0 > content_top and (
                not getattr(self, 'profile_panel', False) or my0 < self._profile_panel_top()):
            screen_r = max(3, self.brush_radius)
            ring_col = BRUSH_COLORS.get(self.brush_mode if not
                       (self.painting and pygame.mouse.get_pressed()[2]) else 'lower',
                       ACCENT_COLOR)
            # Soft fill
            fill_surf = pygame.Surface((screen_r * 2, screen_r * 2), pygame.SRCALPHA)
            pygame.draw.circle(fill_surf, (*ring_col, 18), (screen_r, screen_r), screen_r)
            self.screen.blit(fill_surf, (mx0 - screen_r, my0 - screen_r))
            # Crisp ring
            pygame.draw.circle(self.screen, (*ring_col, 200), (mx0, my0), screen_r, 1)
            # Centre dot
            pygame.draw.circle(self.screen, ring_col, (mx0, my0), 2)

        # ================================================================
        # BOTTOM STATUS BAR
        # ================================================================
        pygame.draw.rect(self.screen, PANEL_COLOR, (0, h - STATUS_H, w, STATUS_H))
        pygame.draw.line(self.screen, BORDER_COLOR, (0, h - STATUS_H), (w, h - STATUS_H), 1)

        # Left: workspace summary
        section_map = {
            'canvas': 'Canvas',
            'project': 'Project',
            'towns': 'Towns',
            'progression': 'Progression',
            'tools': 'Tools',
        }
        mode_label = {'height': 'Heightmap', 'veg': 'Vegetation', 'water': 'Water'}.get(self.mode, self.mode)
        shell_label = section_map.get(self._shell_active_section(), 'Canvas')
        work_mode = (
            'Tile Cleanup' if getattr(self, 'tile_delete_mode', False)
            else ('Edit' if self.edit_mode else 'View')
        )
        left_text = f"{shell_label}  |  {mode_label}  |  {work_mode}"
        if self.mod_project:
            left_text += f"  |  {self.mod_project.name}"
        lr, _ = self.font.render(left_text, TEXT_COLOR)
        self.screen.blit(lr, (10, h - STATUS_H + (STATUS_H - lr.get_height()) // 2))

        # Middle: contextual hint
        if getattr(self, 'tile_delete_mode', False):
            hint = (
                "Drag box | Shift add | Ctrl/right-drag keep | "
                "Select ROW then Invert | Delete moves files to recovery"
            )
        elif self.edit_mode:
            if self.mode == 'height':
                hints = {
                    'raise':   "LMB Raise  |  RMB Lower  |  MMB Sample  |  B cycle brush",
                    'flatten': "LMB Flatten  |  RMB Lower  |  B cycle brush",
                    'paint':   "LMB Paint target  |  MMB set target",
                    'smooth':  "LMB Smooth terrain  |  RMB lower",
                    'noise':   "LMB Add noise  |  RMB subtract noise",
                    'erode':   "LMB Thermal erosion  |  RMB hydraulic erosion",
                }
                hint = hints.get(self.brush_mode, "")
            elif self.mode == 'veg':
                hint = "LMB Paint preset  |  RMB erase  |  MMB sample"
            else:
                hint = "LMB Add water  |  RMB remove water  |  MMB sample"
        else:
            hint = "Scroll zoom  |  drag pan  |  E edit mode  |  ? help"
        hr, _ = self.font.render(hint, DIM_COLOR)
        self.screen.blit(hr, (max(260, w // 2 - hr.get_width() // 2),
                              h - STATUS_H + (STATUS_H - hr.get_height()) // 2))

        # Right: transient status or fallback summary
        if self.status_timer > 0:
            sr2, _ = self.font_big.render(self.status_msg, ACCENT2_COLOR)
            self.screen.blit(sr2, (w - sr2.get_width() - 12,
                                   h - STATUS_H + (STATUS_H - sr2.get_height()) // 2))
            self.status_timer -= 1
        else:
            fallback = f"Undo {len(self.undo_stack)}"
            if self.mod_project and self._mod_undo_stack:
                fallback += f"  |  Mod Undo {len(self._mod_undo_stack)}"
            pending_visible = getattr(self, '_tile_cache_visible_pending', 0)
            if pending_visible > 0:
                fallback += f"  |  Caching {pending_visible} visible"
            sr2, _ = self.font.render(fallback, DIM_COLOR)
            self.screen.blit(sr2, (w - sr2.get_width() - 12,
                                   h - STATUS_H + (STATUS_H - sr2.get_height()) // 2))

        # ================================================================
        # LOADING OVERLAY
        # ================================================================
        if self.loading:
            done, total = self.load_progress
            pct = done / max(total, 1)
            ow, oh = 320, 60
            ox, oy = (w - ow) // 2, (h - oh) // 2
            pygame.draw.rect(self.screen, PANEL_COLOR, (ox, oy, ow, oh), border_radius=8)
            pygame.draw.rect(self.screen, BTN_BORDER,  (ox, oy, ow, oh), 1, border_radius=8)
            msg = f"Loading tiles...  {done}/{total}"
            mr, _ = self.font_big.render(msg, TEXT_COLOR)
            self.screen.blit(mr, (ox + (ow - mr.get_width()) // 2, oy + 10))
            bar_x, bar_y, bar_w, bar_h = ox + 16, oy + 36, ow - 32, 10
            pygame.draw.rect(self.screen, BTN_INACTIVE, (bar_x, bar_y, bar_w, bar_h), border_radius=4)
            pygame.draw.rect(self.screen, ACCENT_COLOR, (bar_x, bar_y, int(bar_w * pct), bar_h), border_radius=4)

        # ================================================================
        # HOVER TOOLTIP
        # ================================================================
        if self.hover_tile and not self.painting and self.show_tile_info:
            tile = self.hover_tile
            lines = [
                f"Tile  ({tile.x}, {tile.y})" + ("  - modified" if tile.dirty else ""),
                f"Height  {tile.min_m:.0f} to {tile.max_m:.0f} m   avg {tile.avg_m:.0f} m",
                f"Vegetation  {tile.dom_preset} - {VEG_NAMES[tile.dom_preset]}",
                f"Water  {tile.water_pct:.1f} %",
            ]
            pad, line_h = 10, 20
            tw = max(self.font.get_rect(l).width for l in lines) + pad * 2
            th = len(lines) * line_h + pad * 2
            tx2 = min(mx0 + 16, w - tw - 4)
            ty2 = max(content_top + 4, min(my0 - 10, h - th - STATUS_H - 4))
            pygame.draw.rect(self.screen, (16, 22, 32), (tx2, ty2, tw, th), border_radius=6)
            pygame.draw.rect(self.screen, BTN_BORDER,   (tx2, ty2, tw, th), 1, border_radius=6)
            mode_col = MODE_COLORS.get(self.mode, ACCENT_COLOR)
            pygame.draw.rect(self.screen, mode_col, (tx2, ty2, 3, th), border_radius=2)
            for i, line in enumerate(lines):
                col3 = (WARN_COLOR if tile.dirty else mode_col) if i == 0 else TEXT_COLOR
                self.font.render_to(self.screen, (tx2 + pad + 4, ty2 + pad + i * line_h), line, col3)

        # ================================================================
        # GENERATE PANEL (overlay)
        # ================================================================
        if self.gen_panel:
            self._draw_generate_panel(self.screen, content_top)

        # ================================================================
        # HELP OVERLAY
        # ================================================================
        if self.show_help:
            self._draw_help_overlay(self.screen)
