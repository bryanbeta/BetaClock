// ============================================================================
//  BetaClock - Translation system (i18n)
//  Languages: en (base), es, pt (Brazil), de, fr, zh (Simplified)
//  Usage: Tr.T("english text")  ->  text in the current language (Tr.Lang)
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BetaClock
{
    public static class Tr
    {
        // 0=en, 1=es, 2=pt, 3=de, 4=fr, 5=zh
        public static int Lang = 0;
        public static readonly string[] Codes = { "en", "es", "pt", "de", "fr", "zh" };
        public static readonly string[] Names = { "English", "Español", "Português (Brasil)", "Deutsch", "Français", "中文 (简体)" };

        // key (en) -> [es, pt, de, fr, zh]
        private static readonly Dictionary<string, string[]> M = new Dictionary<string, string[]>();
        private static bool _init = false;

        public static string CurrentCode() { return (Lang >= 0 && Lang < Codes.Length) ? Codes[Lang] : "en"; }

        public static void SetByCode(string code)
        {
            if (code == null) { Lang = 0; return; }
            code = code.Trim().ToLowerInvariant();
            for (int i = 0; i < Codes.Length; i++) if (Codes[i] == code) { Lang = i; return; }
            Lang = 0; // unknown -> English
        }

        public static string DetectCode()
        {
            try
            {
                string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                for (int i = 0; i < Codes.Length; i++) if (Codes[i] == two) return Codes[i];
                return "en"; // unsupported language -> English
            }
            catch { return "en"; }
        }

        public static string T(string en)
        {
            if (en == null) return "";
            if (Lang == 0) return en; // base language
            EnsureInit();
            string[] v;
            if (M.TryGetValue(en, out v))
            {
                int i = Lang - 1;
                if (i >= 0 && i < v.Length && !string.IsNullOrEmpty(v[i])) return v[i];
            }
            return en; // no translation -> English
        }

        // ---- Localized calendar ----
        // Monday..Sunday (day-of-week column)
        private static readonly string[][] DAYSTRIP = {
            new string[]{ "MON","TUE","WED","THU","FRI","SAT","SUN" },
            new string[]{ "LUN","MAR","MIE","JUE","VIE","SAB","DOM" },
            new string[]{ "SEG","TER","QUA","QUI","SEX","SAB","DOM" },
            new string[]{ "MO","DI","MI","DO","FR","SA","SO" },
            new string[]{ "LUN","MAR","MER","JEU","VEN","SAM","DIM" },
            new string[]{ "一","二","三","四","五","六","日" }
        };
        // Sun..Sat, uppercase (date row)
        private static readonly string[][] DOWUP = {
            new string[]{ "SUN","MON","TUE","WED","THU","FRI","SAT" },
            new string[]{ "DOM","LUN","MAR","MIE","JUE","VIE","SAB" },
            new string[]{ "DOM","SEG","TER","QUA","QUI","SEX","SAB" },
            new string[]{ "SO","MO","DI","MI","DO","FR","SA" },
            new string[]{ "DIM","LUN","MAR","MER","JEU","VEN","SAM" },
            new string[]{ "周日","周一","周二","周三","周四","周五","周六" }
        };
        // Sun..Sat, short lowercase (earnings)
        private static readonly string[][] DOWSHORT = {
            new string[]{ "sun","mon","tue","wed","thu","fri","sat" },
            new string[]{ "dom","lun","mar","mié","jue","vie","sáb" },
            new string[]{ "dom","seg","ter","qua","qui","sex","sáb" },
            new string[]{ "so","mo","di","mi","do","fr","sa" },
            new string[]{ "dim","lun","mar","mer","jeu","ven","sam" },
            new string[]{ "周日","周一","周二","周三","周四","周五","周六" }
        };
        // Sun..Sat, full name (notices)
        private static readonly string[][] DOWFULL = {
            new string[]{ "Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday" },
            new string[]{ "Domingo","Lunes","Martes","Miércoles","Jueves","Viernes","Sábado" },
            new string[]{ "Domingo","Segunda","Terça","Quarta","Quinta","Sexta","Sábado" },
            new string[]{ "Sonntag","Montag","Dienstag","Mittwoch","Donnerstag","Freitag","Samstag" },
            new string[]{ "Dimanche","Lundi","Mardi","Mercredi","Jeudi","Vendredi","Samedi" },
            new string[]{ "周日","周一","周二","周三","周四","周五","周六" }
        };
        // 12 abbreviated months (earnings)
        private static readonly string[][] MON = {
            new string[]{ "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" },
            new string[]{ "ene","feb","mar","abr","may","jun","jul","ago","sep","oct","nov","dic" },
            new string[]{ "jan","fev","mar","abr","mai","jun","jul","ago","set","out","nov","dez" },
            new string[]{ "Jan","Feb","Mär","Apr","Mai","Jun","Jul","Aug","Sep","Okt","Nov","Dez" },
            new string[]{ "janv","févr","mars","avr","mai","juin","juil","août","sept","oct","nov","déc" },
            new string[]{ "1月","2月","3月","4月","5月","6月","7月","8月","9月","10月","11月","12月" }
        };
        // Forex session names (Sydney/Tokyo/London/NY)
        private static readonly string[][] FX = {
            new string[]{ "Sydney","Tokyo","London","NY" },
            new string[]{ "Sídney","Tokio","Londres","NY" },
            new string[]{ "Sídney","Tóquio","Londres","NY" },
            new string[]{ "Sydney","Tokio","London","NY" },
            new string[]{ "Sydney","Tokyo","Londres","NY" },
            new string[]{ "悉尼","东京","伦敦","纽约" }
        };

        private static int LI() { return (Lang >= 0 && Lang < Codes.Length) ? Lang : 0; }
        public static string[] DayStrip() { return DAYSTRIP[LI()]; }
        public static string DowUp(int dow) { return DOWUP[LI()][dow]; }
        public static string DowShort(int dow) { return DOWSHORT[LI()][dow]; }
        public static string DowFull(int dow) { return DOWFULL[LI()][dow]; }
        public static string MonAbbr(int month1to12) { return MON[LI()][month1to12 - 1]; }
        public static string Fx(int i) { return FX[LI()][i]; }

        private static void A(string en, string es, string pt, string de, string fr, string zh)
        { M[en] = new string[] { es, pt, de, fr, zh }; }

        private static void EnsureInit()
        {
            if (_init) return;
            _init = true;

            // ---- Colors ----
            A("White", "Blanco", "Branco", "Weiß", "Blanc", "白色");
            A("Red", "Rojo", "Vermelho", "Rot", "Rouge", "红色");
            A("Green", "Verde", "Verde", "Grün", "Vert", "绿色");
            A("Blue", "Azul", "Azul", "Blau", "Bleu", "蓝色");
            A("Cyan", "Cian", "Ciano", "Cyan", "Cyan", "青色");
            A("Yellow", "Amarillo", "Amarelo", "Gelb", "Jaune", "黄色");
            A("Purple", "Morado", "Roxo", "Lila", "Violet", "紫色");
            A("Orange", "Naranja", "Laranja", "Orange", "Orange", "橙色");
            A("RGB gradient", "RGB degradado", "RGB degradê", "RGB-Verlauf", "Dégradé RVB", "RGB 渐变");
            A("RGB animated", "RGB animado", "RGB animado", "RGB animiert", "RVB animé", "RGB 动态");
            A("Candlesticks", "Velas japonesas", "Velas (candlestick)", "Kerzen (Candlestick)", "Bougies (chandelier)", "K线蜡烛");

            // ---- Menu ----
            A("Color", "Color", "Cor", "Farbe", "Couleur", "颜色");
            A("Brightness", "Brillo", "Brilho", "Helligkeit", "Luminosité", "亮度");
            A("Size", "Tamaño", "Tamanho", "Größe", "Taille", "大小");
            A("Small", "Pequeño", "Pequeno", "Klein", "Petit", "小");
            A("Medium", "Mediano", "Médio", "Mittel", "Moyen", "中");
            A("Large", "Grande", "Grande", "Groß", "Grand", "大");
            A("Huge", "Gigante", "Gigante", "Riesig", "Géant", "超大");
            A("(or mouse wheel)", "(o rueda del ratón)", "(ou roda do mouse)", "(oder Mausrad)", "(ou molette)", "(或鼠标滚轮)");
            A("Opacity", "Opacidad", "Opacidade", "Deckkraft", "Opacité", "不透明度");
            A("24-hour format", "Formato 24 horas", "Formato 24 horas", "24-Stunden-Format", "Format 24 heures", "24 小时制");
            A("Show seconds", "Mostrar segundos", "Mostrar segundos", "Sekunden anzeigen", "Afficher les secondes", "显示秒");
            A("Show day of week", "Mostrar día de la semana", "Mostrar dia da semana", "Wochentag anzeigen", "Afficher le jour", "显示星期");
            A("Show date", "Mostrar fecha", "Mostrar data", "Datum anzeigen", "Afficher la date", "显示日期");
            A("Market session", "Sesión de mercado", "Sessão de mercado", "Marktsitzung", "Séance de marché", "市场时段");
            A("Market", "Mercado", "Mercado", "Markt", "Marché", "市场");
            A("USA (NYSE / options)", "EE.UU. (NYSE / opciones)", "EUA (NYSE / opções)", "USA (NYSE / Optionen)", "USA (NYSE / options)", "美国 (NYSE / 期权)");
            A("Forex (Sydney/Tokyo/London/NY)", "Forex (Sídney/Tokio/Londres/NY)", "Forex (Sídney/Tóquio/Londres/NY)", "Forex (Sydney/Tokio/London/NY)", "Forex (Sydney/Tokyo/Londres/NY)", "外汇 (悉尼/东京/伦敦/纽约)");
            A("Earnings (this week / next)", "Earnings (esta semana / próximo)", "Resultados (esta semana / próximo)", "Earnings (diese Woche / nächste)", "Résultats (cette semaine / prochain)", "财报 (本周 / 下一个)");
            A("Earnings: tickers / refresh...", "Earnings: tickers / actualizar...", "Resultados: tickers / atualizar...", "Earnings: Ticker / aktualisieren...", "Résultats : tickers / actualiser...", "财报：代码 / 刷新...");
            A("Show AM/PM", "Mostrar AM/PM", "Mostrar AM/PM", "AM/PM anzeigen", "Afficher AM/PM", "显示 AM/PM");
            A("Blink the colon", "Parpadeo de los dos puntos", "Piscar os dois pontos", "Doppelpunkt blinken", "Clignotement des deux-points", "冒号闪烁");
            A("Glow", "Halo (glow)", "Brilho (glow)", "Leuchten (Glow)", "Halo (glow)", "辉光");
            A("Digit shadow (background)", "Sombra de dígitos (fondo)", "Sombra dos dígitos (fundo)", "Ziffernschatten (Hintergrund)", "Ombre des chiffres (fond)", "数字背景阴影");
            A("Off", "Apagada", "Desligada", "Aus", "Désactivée", "关闭");
            A("Very faint", "Muy tenue", "Muito fraca", "Sehr schwach", "Très faible", "极淡");
            A("Faint", "Tenue", "Fraca", "Schwach", "Faible", "淡");
            A("Moderate", "Media", "Média", "Mittel", "Moyenne", "中等");
            A("Strong", "Fuerte", "Forte", "Stark", "Forte", "强");
            A("Very strong", "Muy fuerte", "Muito forte", "Sehr stark", "Très forte", "很强");
            A("Maximum", "Máxima", "Máxima", "Maximal", "Maximale", "最大");
            A("Always on top", "Siempre encima", "Sempre no topo", "Immer im Vordergrund", "Toujours au premier plan", "始终置顶");
            A("Lock position", "Bloquear posición", "Bloquear posição", "Position sperren", "Verrouiller la position", "锁定位置");
            A("Start with Windows", "Iniciar con Windows", "Iniciar com o Windows", "Mit Windows starten", "Démarrer avec Windows", "随 Windows 启动");
            A("Sync time over internet", "Sincronizar hora por internet", "Sincronizar hora pela internet", "Zeit über Internet synchronisieren", "Synchroniser l'heure par internet", "通过网络同步时间");
            A("Check time now", "Verificar hora ahora", "Verificar hora agora", "Zeit jetzt prüfen", "Vérifier l'heure maintenant", "立即校时");
            A("About BetaClock...", "Acerca de BetaClock...", "Sobre o BetaClock...", "Über BetaClock...", "À propos de BetaClock...", "关于 BetaClock...");
            A("Language / Idioma", "Idioma / Language", "Idioma / Language", "Sprache / Language", "Langue / Language", "语言 / Language");
            A("Center on screen", "Centrar en pantalla", "Centralizar na tela", "Auf Bildschirm zentrieren", "Centrer à l'écran", "居中显示");
            A("Exit", "Salir", "Sair", "Beenden", "Quitter", "退出");

            // ---- NTP ----
            A("NTP: off", "NTP: desactivado", "NTP: desativado", "NTP: aus", "NTP : désactivé", "NTP：已关闭");
            A("NTP: offline (using PC time)", "NTP: sin conexión (usando hora del PC)", "NTP: sem conexão (usando hora do PC)", "NTP: offline (PC-Zeit)", "NTP : hors ligne (heure du PC)", "NTP：离线（使用电脑时间）");
            A("NTP: PC {0}{1} ms vs internet", "NTP: PC {0}{1} ms vs internet", "NTP: PC {0}{1} ms vs internet", "NTP: PC {0}{1} ms vs Internet", "NTP : PC {0}{1} ms vs internet", "NTP：电脑 {0}{1} 毫秒 vs 网络");

            // ---- Market session ----
            A("OPEN", "ABIERTO", "ABERTO", "OFFEN", "OUVERT", "开盘");
            A("OPEN (1pm close)", "ABIERTO (cierre 1pm)", "ABERTO (fecha 13h)", "OFFEN (Schluss 13 Uhr)", "OUVERT (clôture 13h)", "开盘 (下午1点收盘)");
            A("PRE-MARKET", "PRE-MARKET", "PRÉ-MERCADO", "VORBÖRSLICH", "PRÉ-MARCHÉ", "盘前");
            A("AFTER-HOURS", "AFTER-HOURS", "APÓS-MERCADO", "NACHBÖRSLICH", "APRÈS-BOURSE", "盘后");
            A("CLOSED", "CERRADO", "FECHADO", "GESCHLOSSEN", "FERMÉ", "休市");
            A("WEEKEND", "FIN DE SEMANA", "FIM DE SEMANA", "WOCHENENDE", "WEEK-END", "周末");
            A("HOLIDAY: ", "FERIADO: ", "FERIADO: ", "FEIERTAG: ", "FÉRIÉ : ", "假期：");
            A("closes", "cierra", "fecha", "schließt", "clôture", "收盘");
            A("opens", "abre", "abre", "öffnet", "ouvre", "开盘");
            A("Forex: ", "Forex: ", "Forex: ", "Forex: ", "Forex : ", "外汇：");
            A("Forex closed ", "Forex cerrado ", "Forex fechado ", "Forex geschlossen ", "Forex fermé ", "外汇休市 ");

            // ---- Holiday notice ----
            A("Tomorrow", "Mañana", "Amanhã", "Morgen", "Demain", "明天");
            A("⚠ {0} holiday: {1}", "⚠ {0} feriado: {1}", "⚠ {0} feriado: {1}", "⚠ {0} Feiertag: {1}", "⚠ {0} férié : {1}", "⚠ {0}休市：{1}");
            A("⚠ Tomorrow early close (1 pm)", "⚠ Mañana cierre temprano (1 pm)", "⚠ Amanhã fechamento antecipado (13h)", "⚠ Morgen früher Schluss (13 Uhr)", "⚠ Demain clôture anticipée (13h)", "⚠ 明天提前收盘 (下午1点)");

            // ---- NYSE holidays ----
            A("New Year's Day", "Año Nuevo", "Ano Novo", "Neujahr", "Jour de l'An", "元旦");
            A("MLK Day", "Día de MLK", "Dia de MLK", "MLK-Tag", "Jour de MLK", "马丁·路德·金日");
            A("Presidents' Day", "Día de los Presidentes", "Dia dos Presidentes", "Presidententag", "Jour des présidents", "总统日");
            A("Good Friday", "Viernes Santo", "Sexta-feira Santa", "Karfreitag", "Vendredi saint", "耶稣受难日");
            A("Memorial Day", "Memorial Day", "Memorial Day", "Memorial Day", "Memorial Day", "阵亡将士纪念日");
            A("Juneteenth", "Juneteenth", "Juneteenth", "Juneteenth", "Juneteenth", "六月节");
            A("Independence Day", "Día de la Independencia", "Dia da Independência", "Unabhängigkeitstag", "Fête de l'Indépendance", "独立日");
            A("Labor Day", "Día del Trabajo", "Dia do Trabalho", "Tag der Arbeit", "Fête du Travail", "劳动节");
            A("Thanksgiving", "Acción de Gracias", "Ação de Graças", "Thanksgiving", "Action de grâce", "感恩节");
            A("Christmas", "Navidad", "Natal", "Weihnachten", "Noël", "圣诞节");
            A("Day after Thanksgiving", "Día después de Acción de Gracias", "Dia após a Ação de Graças", "Tag nach Thanksgiving", "Lendemain de Thanksgiving", "感恩节次日");
            A("Christmas Eve", "Víspera de Navidad", "Véspera de Natal", "Heiligabend", "Veille de Noël", "平安夜");
            A("July 3rd (pre-holiday)", "Víspera del 4 de Julio", "Véspera de 4 de Julho", "Vortag des 4. Juli", "Veille du 4 juillet", "独立日前夕");

            // ---- Earnings ----
            A("★ Earnings wk: ", "★ Earnings sem: ", "★ Resultados sem: ", "★ Earnings Wo: ", "★ Résultats sem : ", "★ 本周财报：");
            A("▸ Next: ", "▸ Próx: ", "▸ Próx: ", "▸ Nächste: ", "▸ Suiv. : ", "▸ 下一个：");
            A("today", "hoy", "hoje", "heute", "auj.", "今天");
            A("tomorrow", "mañana", "amanhã", "morgen", "demain", "明天");

            // ---- Alarm manager ----
            A("BetaClock - Alarms", "BetaClock - Alarmas", "BetaClock - Alarmes", "BetaClock - Wecker", "BetaClock - Alarmes", "BetaClock - 闹钟");
            A("Configured alarms:", "Alarmas configuradas:", "Alarmes configurados:", "Eingestellte Wecker:", "Alarmes configurées :", "已设闹钟：");
            A("Time:", "Hora:", "Hora:", "Zeit:", "Heure :", "时间：");
            A("Repeat:", "Repetir:", "Repetir:", "Wiederholen:", "Répéter :", "重复：");
            A("Daily", "Diaria", "Diária", "Täglich", "Quotidien", "每天");
            A("Once", "Una vez", "Uma vez", "Einmal", "Une fois", "一次");
            A("Mon-Fri (market)", "Lun-Vie (mercado)", "Seg-Sex (mercado)", "Mo-Fr (Markt)", "Lun-Ven (marché)", "周一至五 (交易日)");
            A("Mon-Fri", "Lun-Vie", "Seg-Sex", "Mo-Fr", "Lun-Ven", "周一至五");
            A("Label:", "Etiqueta:", "Rótulo:", "Bezeichnung:", "Étiquette :", "标签：");
            A("Active", "Activa", "Ativa", "Aktiv", "Active", "启用");
            A("Add new", "Agregar nueva", "Adicionar", "Neu hinzufügen", "Ajouter", "新增");
            A("Update sel.", "Actualizar sel.", "Atualizar sel.", "Auswahl ändern", "Modifier sél.", "更新所选");
            A("Delete sel.", "Eliminar sel.", "Excluir sel.", "Auswahl löschen", "Supprimer sél.", "删除所选");
            A("Enable / Disable sel.", "Activar / Desactivar sel.", "Ativar / Desativar sel.", "Aktiv./Deaktiv. Ausw.", "Activer / Désactiver sél.", "启用/停用所选");
            A("Test sound", "Probar sonido", "Testar som", "Ton testen", "Tester le son", "测试声音");
            A("Quick presets (clock local time):", "Presets rápidos (hora local del reloj):", "Presets rápidos (hora local do relógio):", "Schnellvorgaben (lokale Uhrzeit):", "Préréglages (heure locale) :", "快捷预设 (本地时间)：");
            A("+ Open 09:30 (M-F)", "+ Apertura 09:30 (L-V)", "+ Abertura 09:30 (S-S)", "+ Eröffnung 09:30 (Mo-Fr)", "+ Ouverture 09:30 (L-V)", "+ 开盘 09:30 (周一至五)");
            A("+ Close 16:00 (M-F)", "+ Cierre 16:00 (L-V)", "+ Fechamento 16:00 (S-S)", "+ Schluss 16:00 (Mo-Fr)", "+ Clôture 16:00 (L-V)", "+ 收盘 16:00 (周一至五)");
            A("Close", "Cerrar", "Fechar", "Schließen", "Fermer", "关闭");
            A("Market open", "Apertura mercado", "Abertura do mercado", "Marktöffnung", "Ouverture du marché", "开盘");
            A("Market close", "Cierre mercado", "Fechamento do mercado", "Marktschluss", "Clôture du marché", "收盘");

            // ---- Alarm alert ----
            A("ALARM", "ALARMA", "ALARME", "WECKER", "ALARME", "闹钟");
            A("Dismiss", "Descartar", "Descartar", "Verwerfen", "Ignorer", "关闭");
            A("Snooze 5 min", "Posponer 5 min", "Adiar 5 min", "5 Min. später", "Rappel 5 min", "稍后 5 分钟");
            A("Alarm", "Alarma", "Alarme", "Wecker", "Alarme", "闹钟");
            A(" (snoozed)", " (pospuesta)", " (adiada)", " (verschoben)", " (reportée)", "（已延后）");
            A("BetaClock - Alarm", "BetaClock - Alarma", "BetaClock - Alarme", "BetaClock - Wecker", "BetaClock - Alarme", "BetaClock - 闹钟");

            // ---- Earnings panel ----
            A("BetaClock - Earnings", "BetaClock - Earnings", "BetaClock - Resultados", "BetaClock - Earnings", "BetaClock - Résultats", "BetaClock - 财报");
            A("Your tickers (comma, space or newline separated):", "Tus tickers (separa por coma, espacio o salto de línea):", "Seus tickers (vírgula, espaço ou quebra de linha):", "Deine Ticker (Komma, Leerzeichen oder Zeilenumbruch):", "Vos tickers (virgule, espace ou saut de ligne) :", "你的代码（用逗号、空格或换行分隔）：");
            A("Save tickers & refresh", "Guardar tickers y actualizar", "Salvar tickers e atualizar", "Ticker speichern & aktualisieren", "Enregistrer & actualiser", "保存并刷新");
            A("Refresh now", "Actualizar ahora", "Atualizar agora", "Jetzt aktualisieren", "Actualiser maintenant", "立即刷新");
            A("Upcoming earnings from your list:", "Próximos earnings de tu lista:", "Próximos resultados da sua lista:", "Kommende Earnings deiner Liste:", "Prochains résultats de votre liste :", "你列表的即将财报：");
            A("(no data yet — click \"Refresh now\")", "(sin datos aún — pulsa \"Actualizar ahora\")", "(sem dados ainda — clique em \"Atualizar agora\")", "(noch keine Daten — \"Jetzt aktualisieren\")", "(pas encore de données — « Actualiser maintenant »)", "（暂无数据 — 点击“立即刷新”）");
            A("Not updated", "Sin actualizar", "Não atualizado", "Nicht aktualisiert", "Non actualisé", "未更新");
            A("Updating...", "Actualizando...", "Atualizando...", "Wird aktualisiert...", "Actualisation...", "更新中...");
            A("Offline, will retry...", "Sin conexión, se reintentará...", "Sem conexão, tentará novamente...", "Offline, wird erneut versucht...", "Hors ligne, nouvelle tentative...", "离线，稍后重试...");
            A("Updating... ({0} days, {1} events)", "Actualizando... ({0} días, {1} eventos)", "Atualizando... ({0} dias, {1} eventos)", "Aktualisiere... ({0} Tage, {1} Ereignisse)", "Actualisation... ({0} jours, {1} événements)", "更新中... ({0} 天, {1} 条)");
            A("{0} earnings · updated {1}", "{0} earnings · actualizado {1}", "{0} resultados · atualizado {1}", "{0} Earnings · aktualisiert {1}", "{0} résultats · maj {1}", "{0} 条财报 · 更新于 {1}");
            A("{0} earnings (cache)", "{0} earnings (cache)", "{0} resultados (cache)", "{0} Earnings (Cache)", "{0} résultats (cache)", "{0} 条财报 (缓存)");
            A("Saved ({0} tickers). Updating...", "Guardado ({0} tickers). Actualizando...", "Salvo ({0} tickers). Atualizando...", "Gespeichert ({0} Ticker). Aktualisiere...", "Enregistré ({0} tickers). Actualisation...", "已保存 ({0} 个代码)。更新中...");

            // ---- About ----
            A("Trading clock · always on top · v1.0", "Reloj de trading · siempre encima · v1.0", "Relógio de trading · sempre no topo · v1.0", "Trading-Uhr · immer im Vordergrund · v1.0", "Horloge de trading · toujours visible · v1.0", "交易时钟 · 始终置顶 · v1.0");

            // ---- Alarm presets ----
            A("Alarms ({0} active)...", "Alarmas ({0} activas)...", "Alarmes ({0} ativos)...", "Wecker ({0} aktiv)...", "Alarmes ({0} actives)...", "闹钟 ({0} 个启用)...");
            A("Alarms...", "Alarmas...", "Alarmes...", "Wecker...", "Alarmes...", "闹钟...");
        }
    }
}
