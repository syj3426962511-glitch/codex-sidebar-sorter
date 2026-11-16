#include <gtk/gtk.h>
#include <libayatana-appindicator/app-indicator.h>
#include <stdatomic.h>
#include <stdbool.h>

static GtkWidget *open_item;
static GtkWidget *exit_item;
static AppIndicator *indicator;
static void (*on_command)(int);
static atomic_bool stopped = false;

static void open_sorter(GtkMenuItem *item, gpointer data) { on_command(1); }
static void exit_sorter(GtkMenuItem *item, gpointer data) { on_command(2); }
static gboolean quit_loop(gpointer data) { gtk_main_quit(); return G_SOURCE_REMOVE; }

struct captions { char *title; char *open; char *exit; };
static gboolean apply_captions(gpointer data) {
    struct captions *text = data;
    if (indicator) app_indicator_set_title(indicator, text->title);
    if (open_item) gtk_menu_item_set_label(GTK_MENU_ITEM(open_item), text->open);
    if (exit_item) gtk_menu_item_set_label(GTK_MENU_ITEM(exit_item), text->exit);
    g_free(text->title); g_free(text->open); g_free(text->exit); g_free(text);
    return G_SOURCE_REMOVE;
}

void sorter_tray_set_captions(const char *title, const char *open, const char *exit) {
    struct captions *text = g_new(struct captions, 1);
    text->title = g_strdup(title); text->open = g_strdup(open); text->exit = g_strdup(exit);
    g_idle_add(apply_captions, text);
}

void sorter_tray_stop(void) {
    atomic_store(&stopped, true);
    g_idle_add(quit_loop, NULL);
}

int sorter_tray_run(const char *icon, const char *title, const char *open, const char *exit, void (*command)(int)) {
    if (!gtk_init_check(NULL, NULL)) return 1;
    on_command = command;
    indicator = app_indicator_new("codex-sidebar-sorter", icon, APP_INDICATOR_CATEGORY_APPLICATION_STATUS);
    app_indicator_set_status(indicator, APP_INDICATOR_STATUS_ACTIVE);
    app_indicator_set_title(indicator, title);
    GtkWidget *menu = gtk_menu_new();
    open_item = gtk_menu_item_new_with_label(open);
    exit_item = gtk_menu_item_new_with_label(exit);
    g_signal_connect(open_item, "activate", G_CALLBACK(open_sorter), NULL);
    g_signal_connect(exit_item, "activate", G_CALLBACK(exit_sorter), NULL);
    gtk_menu_shell_append(GTK_MENU_SHELL(menu), open_item);
    gtk_menu_shell_append(GTK_MENU_SHELL(menu), exit_item);
    gtk_widget_show_all(menu);
    app_indicator_set_menu(indicator, GTK_MENU(menu));
    if (!atomic_load(&stopped)) gtk_main();
    app_indicator_set_status(indicator, APP_INDICATOR_STATUS_PASSIVE);
    g_object_unref(indicator);
    indicator = NULL;
    return 0;
}
