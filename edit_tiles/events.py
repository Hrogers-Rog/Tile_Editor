"""edit_tiles.events Ã¢â‚¬â€ EventsMixin: handle_event sub-methods.

TileEditor.handle_event() routes pygame events to these methods.
"""
import math
import pygame
from mod_project import _bezier_control_points
from .pygame_dialogs import (ask_directory, ask_open_filename,
                             ask_save_filename, ask_string,
                             ask_integer, ask_yes_no)
from .constants import (
    PANEL_H, TOOLBAR_H, STATUS_H,
    HEIGHT_MIN_M, HEIGHT_MAX_M,
    VEG_NAMES,
    _MOD_AVAILABLE, _BRIDGE_AVAILABLE,
)

try:
    from railroader_bridge import RailroaderBridge, preferred_railroader_path
except ImportError:
    RailroaderBridge = None
    preferred_railroader_path = None


class EventsMixin:
    """Event handler sub-methods extracted from TileEditor.handle_event()."""

    def _cancel_pointer_interactions(self):
        """Clear transient mouse interaction state after focus/resize interruptions."""
        if self.painting:
            self._end_stroke()
        self.painting = False
        self._last_paint_pos = None
        self.dragging = False
        self.dragging_node = False
        self.drag_node_id = None
        self.drag_node_origin = None
        self.dragging_spliney_pt = False
        self.profile_drag_node_id = None
        self.profile_drag_preview_y = None
        self._drag_snap_node = None
        self._drag_snap_seg = None
        self.sel_dragging = False
        self.sel_drag_start = None
        self.sel_drag_end = None
        self.sel_lasso_pts = []
        self.group_box_start = None
        self.group_box_end = None
        self.gen_dragging_grid = False

    def _close_workspace_panels(self):
        for attr in (
            'gen_panel', 'mod_panel', 'prog_panel', 'area_panel', 'span_panel',
            'scenery_panel', 'spliney_panel', 'group_panel', 'calc_panel', 'mandela_panel', 'geo_panel'
        ):
            setattr(self, attr, False)
        if hasattr(self, 'spliney_place_mode'):
            self.spliney_place_mode = False

    def _toggle_workspace_panel(self, panel_name):
        attr_map = {
            'generate': 'gen_panel',
            'mod': 'mod_panel',
            'progression': 'prog_panel',
            'areas': 'area_panel',
            'spans': 'span_panel',
            'scenery': 'scenery_panel',
            'spliney': 'spliney_panel',
            'group': 'group_panel',
            'calc': 'calc_panel',
            'mandela': 'mandela_panel',
            'geo': 'geo_panel',
        }
        attr = attr_map[panel_name]
        current = getattr(self, attr)
        self._close_workspace_panels()
        if current:
            return True

        if panel_name == 'generate':
            self.gen_panel = True
            self.gen_view_x = 0.0
            self.gen_view_y = 0.0
            return True
        if panel_name == 'mod':
            if not self.mod_project:
                self.open_mod_folder_dialog()
                return True
            self.mod_panel = True
            return True
        if panel_name == 'progression':
            if not self.mod_project:
                self._set_status("Load a mod first")
                return True
            self._open_progression_editor()
            return True
        if panel_name == 'areas':
            if not self.mod_project:
                self._set_status("Load a mod first")
                return True
            self._open_area_editor()
            return True
        if panel_name == 'spans':
            if not self.mod_project:
                self._set_status("Load a mod first")
                return True
            self.span_panel = True
            self._span_edit_key = None
            self._span_edit_buf = ''
            return True
        if panel_name == 'scenery':
            if not self.mod_project:
                self._set_status("Load a mod first")
                return True
            self.scenery_panel = True
            return True
        if panel_name == 'spliney':
            if not self.mod_project:
                self._set_status("Load a mod first")
                return True
            self.spliney_panel = True
            self._spliney_edit_key = ''
            self._spliney_edit_buf = ''
            return True
        if panel_name == 'group':
            if not self.mod_project:
                self._set_status("Load a mod first")
                return True
            self.group_panel = True
            return True
        if panel_name == 'calc':
            self.calc_panel = True
            return True
        if panel_name == 'mandela':
            if not self.mod_project:
                self._set_status("Load a mod first")
                return True
            self.mandela_panel = True
            return True
        if panel_name == 'geo':
            if not self.mod_project:
                self._set_status("Load a mod first")
                return True
            self.geo_panel = True
            self._geo_tab_rects = []
            self._geo_field_rects = []
            self._geo_choice_rects = []
            self._geo_btn_rects = []
            return True
        return True

    def _load_track_graph_dialog(self):
        try:
            path = ask_open_filename(
                self.screen,
                title="Select track graph JSON",
                filetypes=[("JSON files", "*.json"), ("All files", "*.*")],
                initial_dir=(str(preferred_railroader_path())
                             if preferred_railroader_path else None)
            )
            if path:
                self.load_track_graph(path)
        except Exception as ex:
            self._set_status(f"Failed to load graph: {ex}")

    def _load_tiles_dialog(self):
        try:
            folders = []
            while True:
                folder = ask_directory(
                    self.screen,
                    title=f"Select tile folder to add ({len(folders)+1}) - Cancel when done",
                    initial_dir=(str(preferred_railroader_path())
                                 if preferred_railroader_path else None)
                )
                if not folder:
                    break
                folders.append(folder)
                if not ask_yes_no(
                    self.screen, "Add another?",
                    "Added: " + folder + "\n\nAdd another folder?"
                ):
                    break
            if folders:
                self.load_tiles_folders(folders)
        except Exception as ex:
            self._set_status(f"Load Tiles failed: {ex}")

    def _toggle_edit_mode(self):
        self.edit_mode = not self.edit_mode
        if not self.edit_mode:
            self.select_mode = False
            self.sel_pending_paste = False
        self._set_status("Edit mode ON" if self.edit_mode else "Edit mode OFF")

    def _run_shell_action(self, action):
        if action == 'section_canvas':
            self._close_workspace_panels()
        elif action == 'section_project':
            self._toggle_workspace_panel('mod')
        elif action == 'section_towns':
            self._toggle_workspace_panel('areas')
        elif action == 'section_progression':
            self._toggle_workspace_panel('progression')
        elif action == 'section_tools':
            self._toggle_workspace_panel('geo' if self.mod_project else 'generate')
        elif action == 'open_mod':
            self.open_mod_folder_dialog()
        elif action == 'load_graph':
            self._load_track_graph_dialog()
        elif action == 'load_tiles':
            self._load_tiles_dialog()
        elif action == 'reload_sources':
            self.reload_current_sources()
        elif action == 'toggle_mod':
            self._toggle_workspace_panel('mod')
        elif action == 'open_progression':
            self._toggle_workspace_panel('progression')
        elif action == 'open_areas':
            self._toggle_workspace_panel('areas')
        elif action == 'toggle_spans':
            self._toggle_workspace_panel('spans')
        elif action == 'toggle_scenery':
            self._toggle_workspace_panel('scenery')
        elif action == 'toggle_spliney':
            self._toggle_workspace_panel('spliney')
        elif action == 'toggle_group':
            self._toggle_workspace_panel('group')
        elif action == 'toggle_calc':
            self._toggle_workspace_panel('calc')
        elif action == 'toggle_measure':
            if self.calc_panel and self.calc_mode == 'measure':
                self.calc_panel = False
            else:
                self._close_workspace_panels()
                self.calc_panel = True
                self.calc_mode = 'measure'
        elif action == 'toggle_profile':
            self.profile_panel = not self.profile_panel
            if not self.profile_panel:
                self.profile_drag_node_id = None
                self.profile_drag_preview_y = None
                self.profile_hover_world = None
                self.profile_hover_station_m = None
                self.profile_hover_node_id = None
        elif action == 'toggle_mandela':
            self._toggle_workspace_panel('mandela')
        elif action == 'toggle_geo':
            self._toggle_workspace_panel('geo')
        elif action == 'toggle_generate':
            self._toggle_workspace_panel('generate')
        elif action == 'mode_height':
            self.mode = 'height'
            self.invalidate_all()
        elif action == 'mode_veg':
            self.mode = 'veg'
            self.invalidate_all()
        elif action == 'mode_water':
            self.mode = 'water'
            self.invalidate_all()
        elif action == 'toggle_hillshade':
            self.hillshade = not self.hillshade
            self.invalidate_all()
        elif action == 'fit_view':
            self._fit_view()
        elif action == 'toggle_tracks':
            self.show_tracks = not self.show_tracks
            self._set_status(f"Tracks {'ON' if self.show_tracks else 'OFF'}")
        elif action == 'toggle_nodes':
            if not self.show_tracks:
                self.show_tracks = True
            self.show_nodes = not self.show_nodes
            self._set_status(f"Nodes {'ON' if self.show_nodes else 'OFF'}")
        elif action == 'toggle_elev_colors':
            if not self.show_tracks:
                self.show_tracks = True
            self.show_elev_colors = not self.show_elev_colors
            if self.show_elev_colors:
                self.show_nodes = True
            self._set_status(f"Elevation colors {'ON' if self.show_elev_colors else 'OFF'}")
        elif action == 'toggle_grade_labels':
            if not self.show_tracks:
                self.show_tracks = True
            self.show_grade_labels = not self.show_grade_labels
            self._set_status(f"Grade labels {'ON' if self.show_grade_labels else 'OFF'}")
        elif action == 'toggle_osm':
            self.toggle_osm()
        elif action == 'osm_zoom_in':
            self._adjust_osm_zoom(1)
        elif action == 'osm_zoom_out':
            self._adjust_osm_zoom(-1)
        elif action == 'osm_opacity_up':
            self._adjust_osm_opacity(20)
        elif action == 'osm_opacity_down':
            self._adjust_osm_opacity(-20)
        elif action == 'osm_clear_cache':
            if not self.osm_clear_cache_confirm:
                self.osm_clear_cache_confirm = True
                self._set_status(
                    "Click CONFIRM CLEAR to delete downloaded OSM tiles")
            else:
                files, size = self.osm.clear_disk_cache()
                self.osm_clear_cache_confirm = False
                amount = (
                    f"{size / (1024 * 1024):.1f} MB"
                    if size >= 1024 * 1024
                    else f"{size / 1024:.1f} KB"
                    if size >= 1024
                    else f"{size} B"
                )
                self._set_status(
                    f"Cleared {files:,} OSM tiles ({amount})")
        elif action == 'toggle_diff':
            self.toggle_diff()
        elif action == 'toggle_help':
            self.show_help = not self.show_help
        elif action == 'toggle_edit':
            self._toggle_edit_mode()
        elif action == 'ui_scale_down':
            self._adjust_ui_scale(step=-1)
        elif action == 'ui_scale_reset':
            self._adjust_ui_scale(reset=True)
        elif action == 'ui_scale_up':
            self._adjust_ui_scale(step=1)
        elif action == 'apply_mode_live':
            self._set_live_mod_apply(True)
        elif action == 'apply_mode_batch':
            self._set_live_mod_apply(False)
        elif action == 'apply_pending_mod':
            self._apply_pending_mod_changes()
        elif action == 'save_all':
            self.save_all()
        elif action == 'undo':
            if self.undo_stack:
                self.undo()
        return True

    def _handle_keydown(self, event, mx0, my0, content_top):
        """Handle pygame.KEYDOWN. Returns True if consumed."""
        ctrl = event.mod & pygame.KMOD_CTRL

        # Help overlay: ESC or ? closes it, everything else consumed
        if self.show_help:
            help_page_count = max(1, int(getattr(self, '_help_page_count', 8) or 8))
            if event.key in (pygame.K_ESCAPE, pygame.K_SLASH, pygame.K_F1):
                self.show_help = False
            elif event.key == pygame.K_RIGHT or event.key == pygame.K_TAB:
                self._help_page = (self._help_page + 1) % help_page_count
            elif event.key == pygame.K_LEFT:
                self._help_page = (self._help_page - 1) % help_page_count
            return True

        # Generate panel: type into token field, ESC closes
        if self.gen_panel:
            if event.key == pygame.K_ESCAPE:
                self.gen_panel = False
                self._gen_input_focus = None
                return True
            if self._gen_input_focus == 'token':
                if ctrl and event.key == pygame.K_v:
                    self._paste_generate_token_from_clipboard()
                elif event.key == pygame.K_BACKSPACE:
                    self.gen_token = self.gen_token[:-1]
                elif event.key == pygame.K_RETURN:
                    self._gen_input_focus = None
                elif event.unicode and event.unicode.isprintable():
                    self.gen_token += event.unicode
                return True
            if self._gen_input_focus == 'preset_name':
                if event.key == pygame.K_BACKSPACE:
                    self.gen_preset_name = self.gen_preset_name[:-1]
                elif event.key == pygame.K_RETURN:
                    self._gen_save_preset(self.gen_preset_name)
                    self.gen_preset_name = ""
                    self._gen_input_focus = None
                elif event.unicode and event.unicode.isprintable():
                    self.gen_preset_name += event.unicode
                return True
            return True  # swallow all keys while panel open

        # Group panel keyboard
        if self._group_edit and self._handle_group_keydown(event):
            return True

        # Calculator keyboard
        if self._calc_edit and self._handle_calc_keydown(event):
            return True

        # Mandela keyboard
        if self._mandela_edit and self._handle_mandela_keydown(event):
            return True

        # Spliney props field keyboard input
        if getattr(self,'_spl_edit_key','') and self._handle_spliney_props_keydown(event):
            return True

        # Dedicated spliney panel keyboard input
        if getattr(self, '_spliney_edit_key', '') and self._handle_spliney_panel_keydown(event):
            return True

        # Scenery model field keyboard input
        if getattr(self,'_scenery_edit_model',False) and self._handle_scenery_keydown(event):
            return True

        # Fast scenery placement adjustments while the cursor is over the map.
        if self.scenery_place_mode:
            if event.key in (pygame.K_LEFT, pygame.K_LEFTBRACKET):
                self.scenery_place_rotY = (self.scenery_place_rotY - 15.0) % 360.0
                self._set_status(f"Scenery rotation {self.scenery_place_rotY:.0f} deg")
                return True
            if event.key in (pygame.K_RIGHT, pygame.K_RIGHTBRACKET):
                self.scenery_place_rotY = (self.scenery_place_rotY + 15.0) % 360.0
                self._set_status(f"Scenery rotation {self.scenery_place_rotY:.0f} deg")
                return True
            if event.key == pygame.K_UP:
                self.scenery_place_scale = min(
                    10.0, round(self.scenery_place_scale + 0.1, 2)
                )
                self._set_status(f"Scenery scale {self.scenery_place_scale:.2f}x")
                return True
            if event.key == pygame.K_DOWN:
                self.scenery_place_scale = max(
                    0.1, round(self.scenery_place_scale - 0.1, 2)
                )
                self._set_status(f"Scenery scale {self.scenery_place_scale:.2f}x")
                return True

        # Span field keyboard input
        if getattr(self,'_span_edit_key',None) and self._handle_span_keydown(event):
            return True

        # Property panel field keyboard input
        if self._prop_edit_key and self._handle_prop_keydown(event):
            return True

        # Geo panel keyboard input
        if self.geo_panel and self._handle_geo_keydown(event):
            return True

        # Escape closes dedicated spliney place/panel first
        if event.key == pygame.K_ESCAPE and self.spliney_place_mode:
            self.spliney_place_mode = False
            return True
        if event.key == pygame.K_ESCAPE and self.spliney_panel:
            self.spliney_panel = False
            return True

        # Escape clears spliney selection
        if event.key == pygame.K_ESCAPE and self._spl_edit_key:
            self._spl_edit_key = ''
            self._spl_edit_buf = ''
            return True
        if event.key == pygame.K_ESCAPE and self.sel_spliney_id:
            self.sel_spliney_id = None
            return True

        # Escape closes new panels
        if event.key == pygame.K_ESCAPE and self.mandela_place_mode:
            self.mandela_place_mode = False; return True
        if event.key == pygame.K_ESCAPE and self.mandela_panel:
            self.mandela_panel = False; return True
        if event.key == pygame.K_ESCAPE and self.calc_panel:
            self.calc_panel = False; return True
        if event.key == pygame.K_ESCAPE and self.group_panel:
            self.group_panel = False; return True

        # Escape closes scenery panel / place mode
        if event.key == pygame.K_ESCAPE and self.scenery_place_mode:
            self.scenery_place_mode = False
            return True
        if event.key == pygame.K_ESCAPE and self.scenery_panel:
            self.scenery_panel = False
            return True

        # Escape closes spans panel
        if event.key == pygame.K_ESCAPE and self.span_panel:
            self.span_panel = False
            return True

        # Escape closes geo panel / cancels place mode
        if event.key == pygame.K_ESCAPE and self.geo_panel:
            if self._geo_input_focus:
                self._geo_input_focus = None
                self._geo_input_buf   = ''
            elif getattr(self, '_geo_guide_place_mode', False):
                self._geo_guide_place_mode = False
                self._set_status("Guide trace cancelled")
            elif self._geo_node_place_mode:
                self._geo_node_place_mode = False
                self._set_status("Node placement cancelled")
            else:
                self.geo_panel        = False
                self._clear_geo_preview()
                self._geo_node_place_mode = False
                self._geo_guide_place_mode = False
            return True

        # Escape closes progression/area panels
        if event.key == pygame.K_ESCAPE and (self.prog_panel or self.area_panel):
            self.prog_panel = False
            self.area_panel = False
            return True
        # Escape cancels connect mode
        if event.key == pygame.K_ESCAPE and self._connect_from_node:
            self._connect_from_node = None
            self._set_status("Connect cancelled")
            return True
        # Escape cancels node drag
        if event.key == pygame.K_ESCAPE and self.dragging_node:
            self.dragging_node    = False
            self.drag_node_id     = None
            self.drag_node_origin = None
            self._set_status("Node drag cancelled")
            return True
        # Escape closes mod panel first before quitting
        if event.key == pygame.K_ESCAPE and self.mod_panel:
            self.mod_panel = False
            return True

        if event.key == pygame.K_q:
            return False
        if event.key == pygame.K_ESCAPE:
            return True  # Escape with no panels open is a no-op
        elif event.key == pygame.K_h:
            self.mode = 'height'; self.invalidate_all()
        elif event.key == pygame.K_v:
            self.mode = 'veg'; self.invalidate_all()
        elif event.key == pygame.K_w and not ctrl:
            self.mode = 'water'; self.invalidate_all()
        elif event.key == pygame.K_s and not ctrl:
            self.hillshade = not self.hillshade; self.invalidate_all()
        elif event.key == pygame.K_f:
            self._fit_view()
        elif event.key == pygame.K_o:
            self.toggle_osm()
        elif event.key == pygame.K_d and not ctrl:
            self.toggle_diff()
        elif event.key in (pygame.K_SLASH, pygame.K_F1):
            self.show_help = not self.show_help
        elif ctrl and event.key in (pygame.K_MINUS, pygame.K_KP_MINUS):
            self._adjust_ui_scale(step=-1)
        elif ctrl and event.key in (pygame.K_EQUALS, pygame.K_KP_PLUS):
            self._adjust_ui_scale(step=1)
        elif ctrl and event.key == pygame.K_0:
            self._adjust_ui_scale(reset=True)
        elif ctrl and event.key == pygame.K_r:
            self.reload_current_sources()
        elif event.key == pygame.K_r and not ctrl:
            self.invalidate_all()
            self.invalidate_all()
        elif event.key == pygame.K_t:
            self.show_tracks = not self.show_tracks
            self._set_status(f"Tracks {'ON' if self.show_tracks else 'OFF'}")
        elif event.key == pygame.K_i:
            self.show_tile_info = not self.show_tile_info
            self._set_status(f"Tile info {'ON' if self.show_tile_info else 'OFF'}")
        elif event.key == pygame.K_n:
            self.show_nodes = not self.show_nodes
            self._set_status(f"Nodes {'ON' if self.show_nodes else 'OFF'}")
        elif event.key == pygame.K_l:
            self._load_track_graph_dialog()
        elif event.key == pygame.K_b and _BRIDGE_AVAILABLE:
            # Manually point the bridge at the Railroader game folder
            try:
                folder = ask_directory(self.screen,
                    title="Select Railroader game folder (the one containing Mods/)",
                    initial_dir=(str(preferred_railroader_path())
                                 if preferred_railroader_path else None))
                if folder:
                    if self.bridge:
                        self.bridge.stop()
                    self.bridge = RailroaderBridge(game_dir=folder)
                    self._configure_bridge(self.bridge)
                    self.bridge.on_state_update = self._on_bridge_state
                    self.bridge.on_connect    = lambda: self._set_status("Bridge: connected")
                    self.bridge.on_disconnect = lambda: self._set_status("Bridge: disconnected")
                    self.bridge.start()
                    print(f"[bridge] manually set game_dir = {self.bridge.game_dir}")
                    print(f"[bridge] watching = {self.bridge._state_file}")
                    print(f"[bridge] file exists = {self.bridge._state_file.exists()}")
                    self._set_status(f"Bridge: {self.bridge._state_file}")
            except Exception as ex:
                self._set_status(f"Bridge error: {ex}")
        elif event.key == pygame.K_e:
            self._toggle_edit_mode()
        elif event.key == pygame.K_m and self.edit_mode:
            self.select_mode = not self.select_mode
            if not self.select_mode:
                self.sel_pending_paste = False
            self._set_status("Select tool ON - drag to select" if self.select_mode else "Select tool OFF")
        elif ctrl and event.key == pygame.K_c and self.select_mode:
            self.sel_copy()
        elif ctrl and event.key == pygame.K_x and not self.select_mode:
            self.export_heightmap()
        elif ctrl and event.key == pygame.K_x and self.select_mode:
            self.sel_cut()
        elif ctrl and event.key == pygame.K_v and self.select_mode:
            self.sel_paste_begin()
        elif event.key == pygame.K_DELETE and self.select_mode:
            self.sel_fill(neutral=True)
        elif event.key == pygame.K_ESCAPE and self.select_mode:
            if self.sel_pending_paste:
                self.sel_pending_paste = False
                self._set_status("Paste cancelled")
            elif self.selection:
                self.selection = None
                self._set_status("Selection cleared")
            else:
                self.select_mode = False
        # Edit controls
        elif event.key == pygame.K_b and self.edit_mode:
            modes = ['raise', 'flatten', 'paint', 'smooth', 'noise', 'erode']
            self.brush_mode = modes[(modes.index(self.brush_mode) + 1) % len(modes)]
            extra = (f"  target={self._h16_to_m(self.paint_target):.1f}m"
                     if self.brush_mode == 'paint' and self.paint_target is not None else "")
            self._set_status(f"Brush: {self.brush_mode.upper()}{extra}")
        elif event.key == pygame.K_COMMA and self.edit_mode:  # < key
            if ctrl:
                self.clamp_floor_m = None
                self._set_status("Floor clamp cleared")
            else:
                # Set floor to current cursor height, or nudge down by 10m
                if self.cursor_height_m is not None:
                    self.clamp_floor_m = round(self.cursor_height_m, 1)
                    self._set_status(f"Floor clamp: {self.clamp_floor_m:.1f}m")
                elif self.clamp_floor_m is not None:
                    self.clamp_floor_m = max(HEIGHT_MIN_M, self.clamp_floor_m - 10)
                    self._set_status(f"Floor clamp: {self.clamp_floor_m:.1f}m")
                else:
                    self.clamp_floor_m = HEIGHT_MIN_M + (HEIGHT_MAX_M - HEIGHT_MIN_M) * 0.25
                    self._set_status(f"Floor clamp: {self.clamp_floor_m:.1f}m")
        elif event.key == pygame.K_PERIOD and self.edit_mode:  # > key
            if ctrl:
                self.clamp_ceil_m = None
                self._set_status("Ceiling clamp cleared")
            else:
                if self.cursor_height_m is not None:
                    self.clamp_ceil_m = round(self.cursor_height_m, 1)
                    self._set_status(f"Ceiling clamp: {self.clamp_ceil_m:.1f}m")
                elif self.clamp_ceil_m is not None:
                    self.clamp_ceil_m = min(HEIGHT_MAX_M, self.clamp_ceil_m + 10)
                    self._set_status(f"Ceiling clamp: {self.clamp_ceil_m:.1f}m")
                else:
                    self.clamp_ceil_m = HEIGHT_MAX_M - (HEIGHT_MAX_M - HEIGHT_MIN_M) * 0.25
                    self._set_status(f"Ceiling clamp: {self.clamp_ceil_m:.1f}m")
        elif event.key == pygame.K_LEFTBRACKET:
            self.brush_radius = max(4, self.brush_radius - 4)
        elif event.key == pygame.K_RIGHTBRACKET:
            self.brush_radius = min(200, self.brush_radius + 4)
        elif event.key == pygame.K_MINUS:
            self.brush_strength = max(0.005, round(self.brush_strength - 0.005, 3))
        elif event.key == pygame.K_EQUALS:
            self.brush_strength = min(0.2, round(self.brush_strength + 0.005, 3))
        elif event.key in (pygame.K_0, pygame.K_1, pygame.K_2, pygame.K_3,
                           pygame.K_4, pygame.K_5, pygame.K_6, pygame.K_7):
            self.veg_preset = event.key - pygame.K_0
        elif ctrl and event.key == pygame.K_z:
            if self.mod_project and self._mod_undo_stack:
                self._pop_undo()
            else:
                self.undo()
        elif ctrl and event.key == pygame.K_s:
            if self.mod_panel and self.mod_project:
                self.save_mod_project()
            else:
                self.save_all()
        elif event.key == pygame.K_DELETE and not self.edit_mode:
            if self.mod_project and (self.sel_mod_node_id or self.sel_mod_seg_id):
                self.delete_selected()
                return True
        return True


    def _handle_mousedown(self, event, mx0, my0, content_top, w, h):
        """Handle pygame.MOUSEBUTTONDOWN."""
        mx, my = event.pos

        # ---- Help overlay clicks ----
        if self.show_help and event.button == 1:
            # X close button
            if getattr(self, '_help_close_rect', None) and                         self._help_close_rect.collidepoint(mx, my):
                self.show_help = False
                return True
            # Tab clicks
            for r, idx in getattr(self, '_help_tab_rects', []):
                if r.collidepoint(mx, my):
                    self._help_page = idx
                    return True
            return True  # consume all other clicks

        # ---- Shell welcome / header / sidebar clicks ----
        if event.button == 1:
            for rect, action in getattr(self, '_welcome_action_rects', []):
                if rect.collidepoint(mx, my):
                    if action == 'dismiss_welcome':
                        self._welcome_dismissed = True
                        return True
                    return self._run_shell_action(action)

            sidebar_bounds = getattr(self, '_shell_sidebar_bounds', None)
            if sidebar_bounds and sidebar_bounds.collidepoint(mx, my):
                for rect, action in getattr(self, '_shell_sidebar_rects', []):
                    if rect.collidepoint(mx, my):
                        return self._run_shell_action(action)
                return True

            if my <= PANEL_H:
                for rect, action in getattr(self, '_shell_action_rects', []):
                    if rect.collidepoint(mx, my):
                        return self._run_shell_action(action)
                return True

        # ---- Geometry panel scroll ----
        if self.geo_panel and event.button in (4, 5):
            geo_rect = getattr(self, '_geo_panel_rect', None)
            if geo_rect and geo_rect.collidepoint(mx, my):
                self._scroll_geo_panel(-1 if event.button == 4 else 1)
                return True

        # ---- Progression/Area panel scroll ----
        if (self.prog_panel or self.area_panel) and _MOD_AVAILABLE:
            if event.button == 4:
                if self.prog_panel: self.prog_scroll = max(0, self.prog_scroll - 1)
                if self.area_panel: self.area_scroll = max(0, self.area_scroll - 1)
                return True
            if event.button == 5:
                if self.prog_panel and self.prog_project:
                    self.prog_scroll = min(max(0, len(self.prog_project.sections)-5),
                                           self.prog_scroll + 1)
                if self.area_panel and self.prog_project:
                    self.area_scroll = min(max(0, len(self.prog_project.areas)-5),
                                           self.area_scroll + 1)
                return True

        # ---- Progression panel LMB ----
        if self.prog_panel and event.button == 1 and _MOD_AVAILABLE:
            if self._handle_progression_click(mx, my, content_top):
                return True

        # ---- Area panel LMB ----
        if self.area_panel and event.button == 1 and _MOD_AVAILABLE:
            if self._handle_area_click(mx, my, content_top):
                return True

        # ---- Group panel click ----
        if self.group_panel and event.button == 1 and _MOD_AVAILABLE:
            if self._handle_group_panel_click(mx, my, content_top):
                return True

        # ---- Calc panel click ----
        if self.calc_panel and event.button == 1:
            if self._handle_calc_click(mx, my, content_top):
                return True

        # ---- Mandela panel click ----
        if self.mandela_panel and event.button == 1 and _MOD_AVAILABLE:
            if self._handle_mandela_click(mx, my, content_top):
                return True

        # ---- Dedicated spliney panel click ----
        if self.spliney_panel and event.button == 1 and _MOD_AVAILABLE:
            if self._handle_spliney_panel_click(mx, my, content_top):
                return True

        # ---- Mandela place mode ----
        if self.mandela_place_mode and self.mod_project and event.button == 1:
            if my > content_top:
                self._place_mandela_at(mx, my)
                return True

        # ---- Spliney props click ----
        if self.sel_spliney_id and event.button == 1 and _MOD_AVAILABLE:
            if self._handle_spliney_props_click(mx, my, content_top):
                return True

        # ---- Scenery panel click ----
        if self.scenery_panel and event.button == 1 and _MOD_AVAILABLE:
            if self._handle_scenery_click(mx, my, content_top):
                return True

        # ---- Scenery place mode Ã¢â‚¬â€ click map ----
        if self.scenery_place_mode and self.mod_project and event.button == 1:
            if my > content_top:
                self._place_scenery_at(mx, my)
                return True

        # ---- Dedicated spliney place mode ----
        if self.spliney_place_mode and self.mod_project and event.button == 1:
            if my > content_top:
                self._place_spliney_seed_at(mx, my)
                return True

        # ---- Spans panel click ----
        if self.span_panel and event.button == 1 and _MOD_AVAILABLE:
            if self._handle_span_click(mx, my, content_top):
                return True

        # ---- Geo panel click ----
        if self.geo_panel and event.button == 1 and _MOD_AVAILABLE:
            if self._handle_geo_click(mx, my):
                return True

        # ---- Mod panel scroll (mousewheel) ----
        if self.mod_panel and _MOD_AVAILABLE and self.mod_project:
            if event.button == 4:
                self.mod_layer_scroll = max(0, self.mod_layer_scroll - 1)
                return True
            if event.button == 5:
                max_scroll = max(0, len(self.mod_project.layers) - 5)
                self.mod_layer_scroll = min(max_scroll, self.mod_layer_scroll + 1)
                return True

        # ---- Mod panel LMB ----
        if self.mod_panel and event.button == 1:
            if self.handle_mod_panel_click(mx, my, content_top):
                return True

        if event.button != 1:
            # Non-LMB in panel area: only handle scroll
            if my <= content_top:
                if event.button == 4: self._zoom_at(event.pos, 1.15)
                if event.button == 5: self._zoom_at(event.pos, 1/1.15)
                return True

        # ---- Toolbar click (edit mode) ----
        if self.edit_mode and PANEL_H < my <= PANEL_H + TOOLBAR_H and event.button == 1:
            tbx = 10
            tby_mid = PANEL_H + (TOOLBAR_H - 28) // 2

            # Select tool toggle button Ã¢â‚¬â€ always first
            sel_bw = self.font_big.get_rect("Select").width + 14
            if pygame.Rect(tbx, tby_mid, sel_bw, 28).collidepoint(mx, my):
                self.select_mode = not self.select_mode
                if not self.select_mode:
                    self.sel_pending_paste = False
                self._set_status("Select tool ON - drag to select" if self.select_mode else "Select tool OFF")
                return True
            tbx += sel_bw + 4

            if self.select_mode:
                # Tool type sub-buttons
                tool_labels = {'rect': 'Rect', 'lasso': 'Lasso', 'wand': 'Wand'}
                for st in ['rect', 'lasso', 'wand']:
                    stbw = self.font_big.get_rect(tool_labels[st]).width + 12
                    if pygame.Rect(tbx, tby_mid, stbw, 28).collidepoint(mx, my):
                        self.sel_tool = st
                        self._set_status(f"Select: {st}")
                        return True
                    tbx += stbw + 4

                # Wand tolerance buttons
                if self.sel_tool == 'wand':
                    tbx += 12  # separator
                    wt_lbl = f"Tol {self.sel_wand_tol}"
                    tbx += self.font_big.get_rect(wt_lbl).width + 6
                    if pygame.Rect(tbx, tby_mid+2, 22, 24).collidepoint(mx, my):
                        self.sel_wand_tol = max(50, self.sel_wand_tol - 250); return True
                    tbx += 26
                    if pygame.Rect(tbx, tby_mid+2, 22, 24).collidepoint(mx, my):
                        self.sel_wand_tol = min(10000, self.sel_wand_tol + 250); return True
                    tbx += 26

                tbx += 12  # separator

                # Operation buttons
                has_sel = self.selection is not None
                has_cb  = self.clipboard is not None
                for lbl4, action4, active4 in [
                    ("Copy",     self.sel_copy,        has_sel),
                    ("Cut",      self.sel_cut,         has_sel),
                    ("Paste",    self.sel_paste_begin, has_cb),
                    ("Fill",     self.sel_fill,        has_sel),
                    ("Flip H",   self.sel_mirror_h,    has_sel),
                    ("Flip V",   self.sel_mirror_v,    has_sel),
                    ("Rot 90",   self.sel_rotate_90,   has_sel),
                    ("Deselect", lambda: (setattr(self, 'selection', None),
                                          self._set_status("Selection cleared")), has_sel),
                ]:
                    bw4 = self.font_big.get_rect(lbl4).width + 12
                    if pygame.Rect(tbx, tby_mid, bw4, 28).collidepoint(mx, my):
                        if active4: action4()
                        return True
                    tbx += bw4 + 4
                return True

            tbx += 14  # separator

            if self.mode == 'height':
                for bm, lbl in [('raise','Raise'),('flatten','Flatten'),
                                ('paint','Paint'),('smooth','Smooth'),
                                ('noise','Noise'),('erode','Erode')]:
                    bm_bw = self.font_big.get_rect(lbl).width + 14
                    if pygame.Rect(tbx, tby_mid, bm_bw, 28).collidepoint(mx, my):
                        self.brush_mode = bm
                        self._set_status(f"Brush: {bm.upper()}")
                        return True
                    tbx += bm_bw + 4
                tbx += 16  # separator

            # Size label + [-][+]
            sz_label = f"Size  {self.brush_radius}px"
            tbx += self.font_big.get_rect(sz_label).width + 8
            if pygame.Rect(tbx, tby_mid + 2, 24, 24).collidepoint(mx, my):
                self.brush_radius = max(4, self.brush_radius - 4); return True
            tbx += 28
            if pygame.Rect(tbx, tby_mid + 2, 24, 24).collidepoint(mx, my):
                self.brush_radius = min(200, self.brush_radius + 4); return True
            tbx += 28 + 16  # separator

            if self.mode == 'height':
                # Strength label + [-][+]
                st_label = f"Strength  {self.brush_strength:.3f}"
                tbx += self.font_big.get_rect(st_label).width + 8
                if pygame.Rect(tbx, tby_mid + 2, 24, 24).collidepoint(mx, my):
                    self.brush_strength = max(0.005, round(self.brush_strength - 0.005, 3)); return True
                tbx += 28
                if pygame.Rect(tbx, tby_mid + 2, 24, 24).collidepoint(mx, my):
                    self.brush_strength = min(0.2, round(self.brush_strength + 0.005, 3)); return True
                tbx += 28 + 16  # separator

                # Noise scale [-][+] (only in noise mode, but check regardless for tbx tracking)
                if self.brush_mode == 'noise':
                    # skip clamp labels (not rendered in noise mode's section)
                    ns_label = f"Scale  {self.noise_scale}px"
                    tbx += self.font_big.get_rect(ns_label).width + 8
                    if pygame.Rect(tbx, tby_mid + 2, 24, 24).collidepoint(mx, my):
                        self.noise_scale = max(8, self.noise_scale - 8); return True
                    tbx += 28
                    if pygame.Rect(tbx, tby_mid + 2, 24, 24).collidepoint(mx, my):
                        self.noise_scale = min(256, self.noise_scale + 8); return True

            elif self.mode == 'veg':
                # Skip past "Preset:" label
                tbx += self.font_big.get_rect("Preset:").width + 8
                for i in range(8):
                    if pygame.Rect(tbx, tby_mid + 2, 26, 24).collidepoint(mx, my):
                        self.veg_preset = i
                        self._set_status(f"Preset {i}: {VEG_NAMES[i]}")
                        return True
                    tbx += 29

            return True  # ate the toolbar click

        # ---- Generate panel clicks ----
        if self.gen_panel and my > content_top:
            w2, h2 = self.screen.get_size()
            pw = min(w2 - 40, 1100)
            px2 = (w2 - pw) // 2
            py2 = content_top + 10
            lx2 = px2 + 16
            g = self._gen_grid

            # ---- Preset row clicks (at py2+46, height 30) ----
            preset_row_y = py2 + 46
            ppx2 = lx2 + self.font.get_rect("Presets:").width + 8
            for name in list(self.gen_presets.keys()):
                nb2 = self.font.get_rect(name).width + 16
                pr2 = pygame.Rect(ppx2, preset_row_y - 2, nb2, 22)
                xr2 = pygame.Rect(ppx2 + nb2, preset_row_y, 16, 16)
                if event.button == 1 and pr2.collidepoint(mx, my):
                    self._gen_apply_preset(name)
                    return True
                if event.button == 1 and xr2.collidepoint(mx, my):
                    self._gen_delete_preset(name)
                    return True
                ppx2 += nb2 + 24  # name + x button

            # Save-as name field
            ppx2 += 8 + self.font.get_rect("Save as:").width + 6
            name_rect2 = pygame.Rect(ppx2, preset_row_y - 2, 140, 22)
            if event.button == 1 and name_rect2.collidepoint(mx, my):
                self._gen_input_focus = 'preset_name'
                return True
            ppx2 += 148
            sv_bw3 = self.font.get_rect("Save").width + 16
            sv_r2 = pygame.Rect(ppx2, preset_row_y - 2, sv_bw3, 22)
            if event.button == 1 and sv_r2.collidepoint(mx, my):
                self._gen_save_preset(self.gen_preset_name)
                self.gen_preset_name = ""
                self._gen_input_focus = None
                return True

            # Token field Ã¢â‚¬â€ offset by preset row (30px) + divider (10px)
            tok_y = py2 + 46 + 30 + 10 + 18
            if event.button == 1 and pygame.Rect(lx2, tok_y, 420, 26).collidepoint(mx, my):
                self._gen_input_focus = 'token'
                return True

            # Folder click
            dir_y = tok_y + 32 + 18
            if event.button == 1 and pygame.Rect(lx2, dir_y, 420, 26).collidepoint(mx, my):
                self._gen_input_focus = 'outdir'
                try:
                    folder = ask_directory(self.screen, title="Select output folder")
                    if folder:
                        self.gen_out_dir = folder
                        self._set_status(f"Generate output folder: {folder}")
                except Exception:
                    pass
                return True

            # Options row
            opts_y2 = dir_y + 32
            nlcd_bw2 = self.font_big.get_rect("NLCD land cover").width + 20
            if event.button == 1 and pygame.Rect(lx2, opts_y2, nlcd_bw2, 26).collidepoint(mx, my):
                self.gen_use_nlcd = not self.gen_use_nlcd
                return True
            ox2 = lx2 + nlcd_bw2 + 10
            wlabel_w = self.font.get_rect(f"Workers: {self.gen_workers}").width + 4
            ox2 += wlabel_w
            if event.button == 1 and pygame.Rect(ox2, opts_y2+1, 22, 22).collidepoint(mx, my):
                self.gen_workers = max(1, self.gen_workers - 1); return True
            ox2 += 26
            if event.button == 1 and pygame.Rect(ox2, opts_y2+1, 22, 22).collidepoint(mx, my):
                self.gen_workers = min(32, self.gen_workers + 1); return True
            ox2 += 40
            veg_label_w = self.font.get_rect("Veg override: off").width + 4
            ox2 += veg_label_w
            if event.button == 1 and pygame.Rect(ox2, opts_y2+1, 22, 22).collidepoint(mx, my):
                if self.gen_veg_override is None: self.gen_veg_override = 7
                else: self.gen_veg_override = max(0, self.gen_veg_override - 1)
                return True
            ox2 += 26
            if event.button == 1 and pygame.Rect(ox2, opts_y2+1, 22, 22).collidepoint(mx, my):
                if self.gen_veg_override is None: self.gen_veg_override = 0
                elif self.gen_veg_override >= 7: self.gen_veg_override = None
                else: self.gen_veg_override += 1
                return True

            # Grid area interactions
            if g and g['grid_area'].collidepoint(mx, my):
                stg = g['screen_to_tile_gen']
                gx3, gy3 = stg(mx, my)

                if event.button == 1:
                    # Start box-select drag
                    self.gen_box_start = (gx3, gy3)
                    self.gen_box_end   = (gx3, gy3)
                    return True
                elif event.button == 2:
                    # MMB: start grid pan
                    self.gen_dragging_grid = True
                    self.gen_drag_last = (mx, my)
                    return True
                elif event.button == 3:
                    # Right-click: dequeue single tile or box
                    self.gen_queue.discard((gx3, gy3))
                    return True
                elif event.button == 4:
                    # Scroll up: zoom in
                    self.gen_cell_sz = min(64, self.gen_cell_sz + 2)
                    return True
                elif event.button == 5:
                    # Scroll down: zoom out
                    self.gen_cell_sz = max(8, self.gen_cell_sz - 2)
                    return True

            # Run button
            btn_w2 = 140; btn_h2 = 32
            if g:
                btn_x2 = g['px'] + g['pw'] - btn_w2 - 16
                btn_y2 = g['py'] + g['ph'] - btn_h2 - 12
            else:
                btn_x2 = -999; btn_y2 = -999
            if event.button == 1 and pygame.Rect(btn_x2, btn_y2, btn_w2, btn_h2).collidepoint(mx, my):
                self._gen_start()
                return True

            return True  # ate click inside panel

        profile_rect = getattr(self, '_profile_panel_rect', None)
        if profile_rect and profile_rect.collidepoint(mx, my):
            if event.button == 1:
                for rect, action in getattr(self, '_profile_button_rects', []):
                    if rect.collidepoint(mx, my):
                        if action == 'profile_bench_mark':
                            self._add_profile_benchmark()
                        elif action == 'profile_bench_clear':
                            self._clear_profile_benchmarks()
                        return True
                for rect, node_id, node_y, station_m in getattr(self, '_profile_node_rects', []):
                    if rect.collidepoint(mx, my):
                        self.profile_selected_node_id = node_id
                        self.sel_mod_node_id = node_id
                        self.sel_mod_seg_id = None
                        if self.mod_project:
                            self.profile_drag_node_id = node_id
                            self.profile_drag_origin_y = float(node_y)
                            self.profile_drag_preview_y = float(node_y)
                            self.profile_drag_station_m = float(station_m)
                        self._set_status(f"Profile node {node_id} selected")
                        return True
            return True

        # ---- Canvas click ----
        if my <= content_top:
            return True  # any remaining panel area, ignore

        if getattr(self, '_suspend_canvas_drag', False) and event.button in (1, 2, 3):
            return True

        if not self.edit_mode and event.button == 2:
            self.dragging = True
            self.last_mouse = event.pos
            return True

        if event.button == 3 and getattr(self, '_geo_guide_place_mode', False):
            self._geo_guide_place_mode = False
            self._set_status("Guide trace OFF")
            return True

        if self.edit_mode:
            # Paste-on-click when pending paste
            if self.sel_pending_paste and event.button == 1:
                wr, wc = self.screen_to_wp(mx, my)
                self.sel_paste_commit(wr, wc)
                return True

            # Selection tool clicks
            if self.select_mode and event.button == 1:
                if self.sel_tool == 'wand':
                    self._begin_stroke()
                    self.sel_magic_wand(mx, my)
                    self._end_stroke()
                elif self.sel_tool == 'lasso':
                    self.sel_dragging   = True
                    self.sel_lasso_pts  = [(mx, my)]
                else:  # rect
                    wr, wc = self.screen_to_wp(mx, my)
                    self.sel_dragging   = True
                    self.sel_drag_start = (wr, wc)
                    self.sel_drag_end   = (wr, wc)
                return True

            if not self.select_mode:
                if event.button == 1:
                    self.painting = True
                    self._last_paint_pos = (mx, my)
                    self._begin_stroke(mx, my)
                    self._paint_at(mx, my, erase=False)
                elif event.button == 2:
                    self._sample_at(mx, my)
                elif event.button == 3:
                    self.painting = True
                    self._last_paint_pos = (mx, my)
                    self._begin_stroke(mx, my)
                    self._paint_at(mx, my, erase=True)
                elif event.button == 4:
                    self._zoom_at(event.pos, 1.15)
                elif event.button == 5:
                    self._zoom_at(event.pos, 1/1.15)
        else:
            if event.button == 1:
                # Properties panel clicks (segment rows + action buttons)
                if self._prop_seg_rects or self._prop_action_rects:
                    for rect, sid2 in self._prop_seg_rects:
                        if rect.collidepoint(mx, my):
                            self.sel_mod_seg_id  = sid2
                            self.sel_mod_node_id = None
                            # Find layer for this seg
                            if self.mod_project:
                                for i, l in enumerate(self.mod_project.layers):
                                    if sid2 in l.segments:
                                        self.sel_mod_layer_idx = i
                                        break
                            return True
                    for rect, act in self._prop_action_rects:
                        if rect.collidepoint(mx, my):
                            self._do_prop_action(act)
                            return True

                # Ctrl+drag for group rubber-band selection
                if (pygame.key.get_mods() & pygame.KMOD_CTRL) and self.mod_project and not self.mod_panel:
                    self.group_box_start = (mx, my)
                    self.group_box_end   = (mx, my)
                    # Continue to let normal handling run too

                geo_prefers_track_nodes = bool(
                    self.geo_panel and self.geo_mode in ('curve', 'parallel', 'fit_arc', 'node', 'grade', 'turnout')
                )

                # Spliney point pick / drag start
                if self.mod_project and not self.mod_panel and not geo_prefers_track_nodes:
                    spl_id, spl_pt, spl_li = self.pick_spliney_point(mx, my)
                    if spl_id is not None:
                        if (pygame.key.get_mods() & pygame.KMOD_SHIFT and
                                spl_id == self.sel_spliney_id and
                                spl_li == self.sel_spliney_layer and
                                self.sel_spliney_pt >= 0 and
                                spl_pt != self.sel_spliney_pt):
                            state = self._current_spliney_range_state()
                            anchor_idx = state.get('anchor')
                            if anchor_idx is None:
                                anchor_idx = int(self.sel_spliney_pt)
                            self.sel_spliney_range_id = spl_id
                            self.sel_spliney_range_layer = spl_li
                            self.sel_spliney_range_anchor = int(anchor_idx)
                            self._set_selected_spliney_point(
                                spl_id, spl_li, spl_pt, preserve_range=True
                            )
                            state = self._current_spliney_range_state()
                            if state.get('ready'):
                                self._set_status(
                                    f"Width range selected: {spl_id}[{state['start']}..{state['end']}]"
                                )
                            else:
                                self._set_status(f"Spliney {spl_id}[{spl_pt}] selected")
                            return True
                        if (spl_id == self.sel_spliney_id and
                                spl_pt == self.sel_spliney_pt):
                            # Second click same point Ã¢â€ â€™ start drag
                            self.dragging_spliney_pt = True
                        else:
                            self._set_selected_spliney_point(spl_id, spl_li, spl_pt)
                            self._set_status(
                                f"Spliney {spl_id}[{spl_pt}]  click again to drag")
                        return True

                # Geo panel node place mode Ã¢â‚¬â€ click map to place
                if getattr(self, '_geo_node_place_mode', False) and self.mod_project:
                    self.create_node_at(mx, my)
                    # Keep place mode active so you can place multiple
                    return True

                if getattr(self, '_geo_guide_place_mode', False) and self.mod_project:
                    self._alignment_add_guide_point_at(mx, my)
                    return True

                # Try element pick (mod layers or bridge track)
                if (self.mod_project or self.show_tracks) and not self.mod_panel:
                    ctrl = pygame.key.get_mods() & pygame.KMOD_CTRL
                    kind, eid, li = self.pick_mod_element(mx, my)

                    # Ctrl+click empty space Ã¢â€ â€™ create new node
                    if ctrl and kind is None and self.mod_project:
                        self.create_node_at(mx, my)
                        return True

                    if kind is not None:
                        # Connect mode: Ctrl+click a node Ã¢â€ â€™ finish segment
                        if (ctrl and kind == 'node' and
                                self._connect_from_node and self.mod_project):
                            self.finish_connect(eid)
                            return True

                        # Click same selected node Ã¢â€ â€™ start drag
                        if (kind == 'node' and
                                eid == self.sel_mod_node_id and
                                self.mod_project and
                                self.mod_project.get_graph_layer() is not None):
                            self.dragging_node  = True
                            self.drag_node_id   = eid
                            orig = self.mod_project.merged_nodes.get(eid, {})
                            self.drag_node_origin = dict(orig)
                            self.drag_screen_pos  = (mx, my)
                            return True
                        self.sel_mod_node_id   = eid if kind == 'node' else None
                        self.sel_mod_seg_id    = eid if kind == 'segment' else None
                        self.sel_mod_layer_idx = li
                        if kind == 'node':
                            self.profile_selected_node_id = eid
                            if geo_prefers_track_nodes:
                                self.sel_spliney_id = None
                                self.sel_spliney_pt = -1
                                self.sel_spliney_layer = None
                            if self.geo_panel and self.geo_mode == 'grade' and self.mod_project and not ctrl:
                                if self.grade_chain:
                                    self._extend_grade_chain_to(eid)
                                else:
                                    self._set_grade_chain_start(eid)
                                return True
                        self._prop_edit_key    = None
                        self._prop_edit_buf    = ''
                        # Status bar summary
                        if kind == 'node':
                            if li is not None and self.mod_project:
                                n = self.mod_project.layers[li].nodes[eid]
                                self._set_status(
                                    f"Node {eid}  "
                                    f"({n['x']:.1f}, {n['y']:.1f}, {n['z']:.1f})  "
                                    f"rotY={n['rotY']:.1f}  "
                                    f"layer={self.mod_project.layers[li].label}")
                            else:
                                n = self._get_track_node_state(eid)
                                if n:
                                    source_lbl = '[live bridge]' if n.get('source') == 'bridge' else '[loaded graph]'
                                    self._set_status(
                                        f"Node {eid}  "
                                        f"({n['x']:.1f}, {n['y']:.1f}, {n['z']:.1f})  "
                                        f"rotY={n['rotY']:.1f}  {source_lbl}")
                        else:
                            if li is not None and self.mod_project:
                                s = self.mod_project.layers[li].segments.get(eid, {})
                                self._set_status(
                                    f"Seg {eid}  "
                                    f"{s.get('startId','')} -> {s.get('endId','')}  "
                                    f"{s.get('trackClass','')}  "
                                    f"speed={s.get('speedLimit','')}  "
                                    f"layer={self.mod_project.layers[li].label}")
                            else:
                                s = self._get_track_segment_state(eid)
                                if s:
                                    source_lbl = '[live bridge]' if s.get('source') == 'bridge' else '[loaded graph]'
                                    self._set_status(
                                        f"Seg {eid}  "
                                        f"{s.get('startId','')} -> {s.get('endId','')}  "
                                        f"class={s.get('trackClass','')}  "
                                        f"speed={s.get('speedLimit','')}  {source_lbl}")
                        return True
                    else:
                        self.sel_mod_node_id  = None
                        self.sel_mod_seg_id   = None
                        if not self.profile_drag_node_id:
                            self.profile_selected_node_id = None
                self.dragging = True
                self.last_mouse = event.pos
            elif event.button == 4:
                self._zoom_at(event.pos, 1.15)
            elif event.button == 5:
                self._zoom_at(event.pos, 1/1.15)
        return True


    def _handle_mouseup(self, event, mx0, my0, content_top):
        """Handle pygame.MOUSEBUTTONUP."""
        # Node drag commit
        # Group rubber band release Ã¢â‚¬â€ commit selection
        if event.button == 1 and self.group_box_start and self.group_box_end:
            x0,y0 = self.group_box_start; x1,y1 = self.group_box_end
            if abs(x1-x0) > 4 and abs(y1-y0) > 4 and self.mod_project:
                rx0,rx1 = min(x0,x1),max(x0,x1)
                ry0,ry1 = min(y0,y1),max(y0,y1)
                new_sel = set()
                for nid,node in self.mod_project.merged_nodes.items():
                    if node.get('deleted'): continue
                    snx,sny = self.unity_to_screen(node['x'],node['z'])
                    if rx0<=snx<=rx1 and ry0<=sny<=ry1:
                        new_sel.add(nid)
                if pygame.key.get_mods() & pygame.KMOD_SHIFT:
                    self.group_sel_ids |= new_sel
                else:
                    self.group_sel_ids = new_sel
                self._set_status(f"Group: {len(self.group_sel_ids)} nodes selected")
            self.group_box_start = None; self.group_box_end = None
            return True

        if event.button == 1 and self.profile_drag_node_id:
            node_id = self.profile_drag_node_id
            origin_y = float(getattr(self, 'profile_drag_origin_y', 0.0))
            new_y = float(getattr(self, 'profile_drag_preview_y', origin_y) or origin_y)
            self.profile_drag_node_id = None
            self.profile_drag_origin_y = 0.0
            self.profile_drag_preview_y = None
            if abs(new_y - origin_y) > 0.01:
                self._commit_profile_node_y(node_id, new_y)
            return True

        # Spliney drag release
        if event.button == 1 and self.dragging_spliney_pt and self.sel_spliney_id:
            sx2, sy2 = self.drag_screen_pos
            ux2, uz2 = self.screen_to_unity(sx2, sy2)
            self._commit_spliney_drag(
                self.sel_spliney_id, self.sel_spliney_layer,
                self.sel_spliney_pt, ux2, uz2)
            self.dragging_spliney_pt = False
            return True

        if event.button == 1 and self.dragging_node and self.drag_node_id:
            sx, sy   = self.drag_screen_pos
            nid      = self.drag_node_id
            orig     = self.drag_node_origin or {}
            orig_sx, orig_sy = self.unity_to_screen(orig.get('x',0), orig.get('z',0))
            moved    = ((sx-orig_sx)**2+(sy-orig_sy)**2)**0.5

            snap_node = getattr(self, '_drag_snap_node', None)
            snap_seg  = getattr(self, '_drag_snap_seg', None)

            if snap_node and self.mod_project and moved > 2:
                # Drag onto node Ã¢â€ â€™ create segment between them
                self._push_undo(f"connect drag {nid}->{snap_node}")
                graph = self.mod_project.get_graph_layer()
                if graph:
                    import math as _mc
                    n_a = self.mod_project.merged_nodes.get(nid, {})
                    n_b = self.mod_project.merged_nodes.get(snap_node, {})
                    # Point both nodes toward each other so bezier stays straight
                    if n_a and n_b:
                        dx_ab = n_b['x']-n_a['x']; dz_ab = n_b['z']-n_a['z']
                        ab_rotY = _mc.degrees(_mc.atan2(dx_ab, dz_ab)) % 360
                        ba_rotY = (ab_rotY + 180) % 360
                        graph.set_node(nid, n_a['x'], n_a['y'], n_a['z'],
                                       n_a.get('rotX',0), ab_rotY,
                                       n_a.get('rotZ',0), n_a.get('flipSwitchStand',False))
                        graph.set_node(snap_node, n_b['x'], n_b['y'], n_b['z'],
                                       n_b.get('rotX',0), ba_rotY,
                                       n_b.get('rotZ',0), n_b.get('flipSwitchStand',False))
                    sid2 = self.mod_project.next_seg_id()
                    graph.set_segment(sid2, nid, snap_node,
                                      'Mainline','Standard',45,0,'',
                                      getattr(self, 'geo_gauge', 'Standard'))
                    self.mod_project._rebuild_merge()
                    graph.save()
                    if self.bridge: self.bridge.reload_tracks(str(graph.path))
                    self.sel_mod_seg_id  = sid2
                    self.sel_mod_node_id = None
                    self._set_status(f"Connected {nid} -> {snap_node}  [{sid2}]")

            elif snap_seg and self.mod_project and moved > 2:
                seg_id2, seg_li = snap_seg
                shift_held = pygame.key.get_mods() & pygame.KMOD_SHIFT
                if shift_held:
                    turnout_error = self._turnout_settings_error()
                    if turnout_error:
                        self._set_status(turnout_error)
                        self.dragging_node = False
                        self.drag_node_id = None
                        self.drag_node_origin = None
                        self._drag_snap_node = None
                        self._drag_snap_seg = None
                        return True
                self._push_undo(
                    f"turnout {nid} into {seg_id2}"
                    if shift_held else
                    f"insert {nid} into {seg_id2}"
                )

                # Snap node position onto the segment line first (no save yet)
                import math as _ms
                seg_obj = self.mod_project.merged_segments.get(seg_id2)
                if seg_obj:
                    n0s = self.mod_project.merged_nodes.get(seg_obj.get('startId',''))
                    n1s = self.mod_project.merged_nodes.get(seg_obj.get('endId',''))
                    if n0s and n1s:
                        dx2 = n1s['x']-n0s['x']; dz2 = n1s['z']-n0s['z']
                        seg_len = _ms.sqrt(dx2*dx2+dz2*dz2)
                        if seg_len > 0.01:
                            ux2, uz2 = self.screen_to_unity(sx, sy)
                            t = max(0,min(1,((ux2-n0s['x'])*dx2+(uz2-n0s['z'])*dz2)/(seg_len*seg_len)))
                            snap_x = n0s['x'] + t*dx2
                            snap_z = n0s['z'] + t*dz2
                            p0, p1, p2, p3 = _bezier_control_points(n0s, n1s)
                            omt = 1.0 - t
                            snap_y = (
                                omt**3 * p0[1]
                                + 3.0 * omt**2 * t * p1[1]
                                + 3.0 * omt * t**2 * p2[1]
                                + t**3 * p3[1]
                            )
                            # Write position directly without save/rebuild
                            graph2 = self.mod_project.get_graph_layer()
                            if graph2:
                                nd2 = self.mod_project.merged_nodes.get(nid, {})
                                seg_rotY = self._bezier_tangent_rotY(n0s, n1s, t)
                                seg_rotX = self._bezier_tangent_rotX(n0s, n1s, t)
                                graph2.set_node(nid, snap_x, snap_y, snap_z,
                                               seg_rotX, seg_rotY,
                                               nd2.get('rotZ',0),
                                               nd2.get('flipSwitchStand',False))

                if shift_held:
                    self._insert_turnout_into_segment(nid, seg_id2)
                    # Select the switch node so it's visible
                    self.sel_mod_node_id = nid
                    self.sel_mod_seg_id  = None
                else:
                    self._insert_node_into_segment(nid, seg_id2)

            elif moved > 2:
                # Normal move - Shift constrains to X or Z axis, baseline locks apply otherwise.
                sx2, sy2 = sx, sy
                shift_held = pygame.key.get_mods() & pygame.KMOD_SHIFT
                if shift_held:
                    orig_sx2, orig_sy2 = self.unity_to_screen(
                        orig.get('x', 0), orig.get('z', 0))
                    dx_scr = abs(sx2 - orig_sx2)
                    dy_scr = abs(sy2 - orig_sy2)
                    if dx_scr >= dy_scr:
                        sy2 = orig_sy2   # lock to horizontal (X axis)
                    else:
                        sx2 = orig_sx2   # lock to vertical (Z axis)
                new_ux, new_uz = self.screen_to_unity(sx2, sy2)
                if not shift_held:
                    anchor = {
                        'id': nid,
                        'x': orig.get('x', 0.0),
                        'y': orig.get('y', 0.0),
                        'z': orig.get('z', 0.0),
                        'rotY': orig.get('rotY', 0.0),
                        'source': 'drag',
                    }
                    new_ux, new_uz, _lock_info = self._apply_measure_constraints(
                        new_ux, new_uz, anchor=anchor)
                self._commit_node_drag(nid, new_ux, new_uz)

            self.dragging_node    = False
            self.drag_node_id     = None
            self.drag_node_origin = None
            self._drag_snap_node  = None
            self._drag_snap_seg   = None
            return True

        # Selection drag commit Ã¢â‚¬â€ branch by tool type
        if event.button == 1 and self.sel_dragging and self.select_mode:
            if self.sel_tool == 'lasso':
                self._sel_commit_lasso()
            else:
                self._sel_commit_drag()
            return True
        # Box-select: commit queued tiles on release
        if event.button == 1 and self.gen_panel and self.gen_box_start:
            if self.gen_box_end:
                bsx, bsy = self.gen_box_start
                bex, bey = self.gen_box_end
                x0, x1 = sorted([bsx, bex])
                y0, y1 = sorted([bsy, bey])
                for gy4 in range(y0, y1 + 1):
                    for gx4 in range(x0, x1 + 1):
                        if (gx4, gy4) not in self.gen_running:
                            self.gen_queue.add((gx4, gy4))
                n = (x1-x0+1) * (y1-y0+1)
                self._set_status(f"Queued {n} tile{'s' if n!=1 else ''}")
            self.gen_box_start = None
            self.gen_box_end   = None
            return True
        if event.button == 2:
            self.gen_dragging_grid = False
        if event.button in (1, 2, 3):
            if self.painting:
                self._end_stroke()
                self.painting = False
                self._last_paint_pos = None
            self.dragging = False
            if not any(pygame.mouse.get_pressed(5)[:3]):
                self._suspend_canvas_drag = False
        return True


    def _handle_mousemotion(self, event, mx0, my0, content_top):
        """Handle pygame.MOUSEMOTION."""
        mx, my = event.pos
        buttons = pygame.mouse.get_pressed(5)
        if getattr(self, '_suspend_canvas_drag', False) and not any(buttons[:3]):
            self._suspend_canvas_drag = False
        if (not any(buttons[:3]) and (
                self.dragging or
                self.dragging_node or
                self.dragging_spliney_pt or
                self.profile_drag_node_id or
                self.sel_dragging or
                self.group_box_start or
                self.gen_dragging_grid or
                self.painting)):
            self._cancel_pointer_interactions()

        if self.profile_drag_node_id:
            plot_rect = getattr(self, '_profile_plot_rect', None)
            profile_data = getattr(self, '_profile_last_data', None) or {}
            if plot_rect and profile_data:
                y_min = float(profile_data.get('y_min', 0.0))
                y_max = float(profile_data.get('y_max', y_min + 1.0))
                frac = (plot_rect.bottom - my) / max(1, plot_rect.height)
                frac = max(0.0, min(1.0, frac))
                self.profile_drag_preview_y = y_min + frac * (y_max - y_min)
            return True

        # Node drag motion
        if self.dragging_node and self.drag_node_id:
            self.drag_screen_pos = (mx, my)
            return True

        # Spliney point drag motion
        if self.dragging_spliney_pt and self.sel_spliney_id:
            self.drag_screen_pos = (mx, my)
            return True

        # Group rubber band motion
        if self.group_box_start and not self.dragging_node:
            self.group_box_end = (mx, my)

        # Generate panel: update box-select end and handle grid pan
        if self.gen_panel:
            if self.gen_box_start and pygame.mouse.get_pressed()[0]:
                g = self._gen_grid
                if g and g['grid_area'].collidepoint(mx, my):
                    self.gen_box_end = g['screen_to_tile_gen'](mx, my)
            if self.gen_dragging_grid:
                dx = mx - self.gen_drag_last[0]
                dy = my - self.gen_drag_last[1]
                self.gen_view_x += dx
                self.gen_view_y += dy
                self.gen_drag_last = (mx, my)
            return True

        if self.painting and self.edit_mode and not self.select_mode:
            btn = pygame.mouse.get_pressed()
            step = max(1, self.brush_radius // 2)
            lp = self._last_paint_pos
            if lp is None or math.hypot(mx - lp[0], my - lp[1]) >= step:
                self._paint_at(mx, my, erase=btn[2])
                self._last_paint_pos = (mx, my)
        elif self.sel_dragging and self.select_mode:
            if self.sel_tool == 'lasso':
                # Collect screen points Ã¢â‚¬â€ only add when moved enough to avoid duplicate spam
                if (not self.sel_lasso_pts or
                        math.hypot(mx - self.sel_lasso_pts[-1][0],
                                   my - self.sel_lasso_pts[-1][1]) >= 4):
                    self.sel_lasso_pts.append((mx, my))
            else:  # rect
                wr, wc = self.screen_to_wp(mx, my)
                self.sel_drag_end = (wr, wc)
        elif self.dragging and not self.edit_mode:
            dx = mx - self.last_mouse[0]
            dy = my - self.last_mouse[1]
            self.pan_x += dx
            self.pan_y += dy
            self.last_mouse = event.pos

        if my > content_top:
            tx, ty = self.screen_to_tile(mx, my)
            self.hover_tile = self.tiles.get(f'{tx},{ty}')
        else:
            self.hover_tile = None
        return True


    def _handle_mousewheel(self, event, mx0, my0, content_top):
        """Handle pygame.MOUSEWHEEL."""
        mx, my = pygame.mouse.get_pos()
        # Generate panel scroll = zoom grid
        if self.gen_panel:
            g = self._gen_grid
            if g and g['grid_area'].collidepoint(mx, my):
                old_sz = self.gen_cell_sz
                self.gen_cell_sz = max(8, min(64, self.gen_cell_sz + (2 if event.y > 0 else -2)))
                new_sz = self.gen_cell_sz
                ga = g['grid_area']
                # Tile coordinate under cursor must not move after zoom
                # tile_x = (mx - ga.x - view_x) / old_sz  Ã¢â€ â€™  view_x = (mx - ga.x) - tile_x * new_sz
                tile_x = (mx - ga.x - self.gen_view_x) / max(1, old_sz)
                tile_y = (ga.y + self.gen_view_y - my) / max(1, old_sz)
                self.gen_view_x = (mx - ga.x) - tile_x * new_sz
                self.gen_view_y = (my - ga.y) + tile_y * new_sz
            return True
        profile_rect = getattr(self, '_profile_panel_rect', None)
        if profile_rect and profile_rect.collidepoint(mx, my):
            return True
        ctrl = pygame.key.get_mods() & pygame.KMOD_CTRL
        if self.edit_mode and ctrl:
            # Ctrl+scroll Ã¢â€ â€™ resize brush
            step = max(2, self.brush_radius // 8)
            if event.y > 0:
                self.brush_radius = min(200, self.brush_radius + step)
            else:
                self.brush_radius = max(4, self.brush_radius - step)
        else:
            factor = 1.15 if event.y > 0 else 1/1.15
            self._zoom_at((mx, my), factor)
        return True
