import com.dabomstew.pkrandom.Settings;
import com.dabomstew.pkrandom.cli.CliRandomizer;
import com.dabomstew.pkrandom.romhandlers.*;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.PrintStream;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

/**
 * Wrapper around the randomizer's CLI mode. Same arguments as "cli" (-s settings -i rom -o out [-l]).
 *
 * The CLI applies settings files as-is, while the GUI disables options the loaded ROM does not support.
 * "Correct static music" only exists for some ROM versions (e.g. not for German Black/White/Platinum), and
 * the CLI crashes with an ArrayIndexOutOfBoundsException when a settings file enables it anyway.
 * This wrapper does what the GUI does: turn it off for such ROMs, then run the normal CLI.
 *
 * Also used by the app's settings editor:
 *   settings-dump  <file.rnqs>                      -> one line per option: name TAB type TAB value TAB options
 *   settings-write <in.rnqs> <out.rnqs> NAME=VALUE... -> applies the values and saves via Settings.write
 */
public class RandoHelper {
    public static void main(String[] args) throws Exception {
        if (args.length >= 2 && args[0].equals("settings-dump")) {
            dumpSettings(args[1]);
            return;
        }
        if (args.length >= 3 && args[0].equals("settings-write")) {
            writeSettings(args);
            return;
        }

        String settingsPath = null, romPath = null;
        for (int i = 0; i < args.length - 1; i++) {
            if (args[i].equals("-s")) settingsPath = args[i + 1];
            if (args[i].equals("-i")) romPath = args[i + 1];
        }
        if (settingsPath == null || romPath == null) {
            System.exit(CliRandomizer.invoke(args));
        }

        Settings settings;
        try (FileInputStream in = new FileInputStream(settingsPath)) {
            settings = Settings.read(in);
        }

        boolean changed = false;
        RomHandler rom = loadRom(romPath);
        if (rom != null) changed |= applyRomCapabilities(settings, rom);
        changed |= applyEveryLevelEvolutionRules(settings);

        File tempSettings = null;
        if (changed) {
            tempSettings = File.createTempFile("randoapp_", ".rnqs");
            try (FileOutputStream out = new FileOutputStream(tempSettings)) {
                settings.write(out);
            }
            for (int i = 0; i < args.length - 1; i++) {
                if (args[i].equals("-s")) args[i + 1] = tempSettings.getAbsolutePath();
            }
        }

        int exit;
        try {
            exit = CliRandomizer.invoke(args);
        } finally {
            if (tempSettings != null) tempSettings.delete();
        }
        System.exit(exit);
    }

    /**
     * "Random every level" evolutions can form cycles (A -> B -> A). The GUI therefore turns off everything that
     * walks evolution chains when that mode is selected; with those options the CLI loops forever / runs out of
     * memory (e.g. random base stats + "follow evolutions"). Mirrors NewRandomizerGUI's every-level handling.
     */
    static boolean applyEveryLevelEvolutionRules(Settings s) throws Exception {
        if (s.getEvolutionsMod() != Settings.EvolutionsMod.RANDOM_EVERY_LEVEL) return false;
        List<String> off = new ArrayList<>();
        String[] followFlags = {
            "BaseStatsFollowEvolutions", "AbilitiesFollowEvolutions", "TmsFollowEvolutions", "TutorFollowEvolutions",
            "BaseStatsFollowMegaEvolutions", "TypesFollowMegaEvolutions", "AbilitiesFollowMegaEvolutions",
            "ChangeImpossibleEvolutions", "MakeEvolutionsEasier", "RemoveTimeBasedEvolutions", "TrainersForceFullyEvolved",
        };
        for (String flag : followFlags) {
            Method get = Settings.class.getMethod("is" + flag);
            if ((Boolean) get.invoke(s)) {
                Settings.class.getMethod("set" + flag, boolean.class).invoke(s, false);
                off.add(flag);
            }
        }
        if (s.getTypesMod() == Settings.TypesMod.RANDOM_FOLLOW_EVOLUTIONS) {
            setEnum(s, "TypesMod", Settings.TypesMod.COMPLETELY_RANDOM);
            off.add("TypesMod=RANDOM_FOLLOW_EVOLUTIONS -> COMPLETELY_RANDOM");
        }
        if (s.getStartersMod() == Settings.StartersMod.RANDOM_WITH_TWO_EVOLUTIONS) {
            setEnum(s, "StartersMod", Settings.StartersMod.COMPLETELY_RANDOM);
            off.add("StartersMod=RANDOM_WITH_TWO_EVOLUTIONS -> COMPLETELY_RANDOM");
        }
        // Not guarded by the GUI, but crashes (StackOverflowError in setIllegalEvos, ~75% of runs): marking the
        // rival's starter chain as illegal for "unique Elite Four Pokemon" recurses forever through evolution cycles.
        if (s.getEliteFourUniquePokemonNumber() > 0 && s.isRivalCarriesStarterThroughout()) {
            s.setEliteFourUniquePokemonNumber(0);
            off.add("EliteFourUniquePokemonNumber (zusammen mit RivalCarriesStarterThroughout)");
        }
        if (off.isEmpty()) return false;
        System.out.println("Hinweis: \"Entwicklungen zufaellig (jedes Level)\" vertraegt sich nicht mit: " + off
            + " -> angepasst (wie in der GUI).");
        return true;
    }

    static void setEnum(Settings s, String prop, Enum<?> value) throws Exception {
        Method setter = Settings.class.getDeclaredMethod("set" + prop, value.getDeclaringClass());
        setter.setAccessible(true);
        setter.invoke(s, value);
    }

    static Settings readSettings(String path) throws Exception {
        try (FileInputStream in = new FileInputStream(path)) {
            return Settings.read(in);
        }
    }

    /** Simple options only: boolean, int, enum (+ the ROM name the file was made with, read-only). */
    static List<Method> editableGetters() {
        List<Method> result = new ArrayList<>();
        Method[] all = Settings.class.getMethods();
        java.util.Arrays.sort(all, (a, b) -> a.getName().compareTo(b.getName()));
        for (Method g : all) {
            String n = g.getName();
            if (g.getParameterCount() != 0 || n.equals("getClass")) continue;
            if (!n.startsWith("is") && !n.startsWith("get")) continue;
            Class<?> t = g.getReturnType();
            if (t == boolean.class || t == int.class || t.isEnum() || n.equals("getRomName")) result.add(g);
        }
        return result;
    }

    static String propName(Method getter) {
        String n = getter.getName();
        return n.startsWith("is") ? n.substring(2) : n.substring(3);
    }

    static void dumpSettings(String path) throws Exception {
        Settings s = readSettings(path);
        PrintStream out = new PrintStream(System.out, true, "UTF-8");
        for (Method g : editableGetters()) {
            Class<?> t = g.getReturnType();
            Object v = g.invoke(s);
            String type = t == boolean.class ? "bool" : t == int.class ? "int" : t.isEnum() ? "enum" : "string";
            StringBuilder options = new StringBuilder();
            if (t.isEnum()) {
                for (Object c : t.getEnumConstants()) {
                    if (options.length() > 0) options.append(',');
                    options.append(((Enum<?>) c).name());
                }
            }
            String value = v == null ? "" : v instanceof Enum ? ((Enum<?>) v).name() : String.valueOf(v);
            out.println(propName(g) + "\t" + type + "\t" + value.replace('\t', ' ') + "\t" + options);
        }
    }

    static void writeSettings(String[] args) throws Exception {
        Settings s = readSettings(args[1]);
        String outPath = args[2];
        for (int i = 3; i < args.length; i++) {
            int eq = args[i].indexOf('=');
            String prop = args[i].substring(0, eq), value = args[i].substring(eq + 1);
            Method getter = null;
            for (Method g : editableGetters()) if (propName(g).equals(prop)) getter = g;
            if (getter == null || prop.equals("RomName")) throw new IllegalArgumentException("Unbekannte Option: " + prop);
            Class<?> t = getter.getReturnType();
            Object parsed;
            if (t == boolean.class) parsed = Boolean.parseBoolean(value);
            else if (t == int.class) parsed = Integer.parseInt(value);
            else parsed = Enum.valueOf(t.asSubclass(Enum.class), value);
            // Enum setters are private in Settings (public ones take boolean... flags), hence getDeclaredMethod.
            Method setter = Settings.class.getDeclaredMethod("set" + prop, t);
            setter.setAccessible(true);
            setter.invoke(s, parsed);
        }
        try (FileOutputStream out = new FileOutputStream(outPath)) {
            s.write(out);
        }
        System.out.println("OK");
    }

    /** Loads the ROM with the matching handler (null if no handler recognises it – the CLI will report that). */
    static RomHandler loadRom(String romPath) {
        RomHandler.Factory[] factories = {
            new Gen1RomHandler.Factory(), new Gen2RomHandler.Factory(), new Gen3RomHandler.Factory(),
            new Gen4RomHandler.Factory(), new Gen5RomHandler.Factory(), new Gen6RomHandler.Factory(),
            new Gen7RomHandler.Factory()
        };
        for (RomHandler.Factory f : factories) {
            if (f.isLoadable(romPath)) {
                RomHandler handler = f.create(new Random());
                return handler.loadRom(romPath) ? handler : null;
            }
        }
        return null;
    }

    /**
     * The GUI greys out options the loaded game can't do (based on these same RomHandler checks); the CLI applies
     * them anyway and can crash, e.g. "Correct Static Music" on German ROMs or starter alt formes in Gen 4.
     */
    static boolean applyRomCapabilities(Settings s, RomHandler rom) throws Exception {
        List<String> off = new ArrayList<>();
        if (!rom.hasStaticMusicFix()) flagsOff(s, off, "CorrectStaticMusic");
        if (!rom.hasStarterAltFormes()) flagsOff(s, off, "AllowStarterAltFormes");
        if (!rom.hasStaticAltFormes()) flagsOff(s, off, "AllowStaticAltFormes");
        if (!rom.hasWildAltFormes()) flagsOff(s, off, "AllowWildAltFormes");
        if (!rom.supportsStarterHeldItems()) flagsOff(s, off, "RandomizeStartersHeldItems", "BanBadRandomStarterHeldItems");
        if (!rom.hasTimeBasedEncounters()) flagsOff(s, off, "UseTimeBasedEncounters");
        if (!rom.hasMainGameLegendaries()) flagsOff(s, off, "LimitMainGameLegendaries");
        if (!rom.hasPhysicalSpecialSplit()) flagsOff(s, off, "RandomizeMoveCategory");
        if (!rom.supportsFourStartingMoves()) flagsOff(s, off, "StartWithGuaranteedMoves");
        if (!rom.hasMegaEvolutions()) flagsOff(s, off, "SwapStaticMegaEvos", "SwapTrainerMegaEvos",
            "BaseStatsFollowMegaEvolutions", "TypesFollowMegaEvolutions", "AbilitiesFollowMegaEvolutions");
        if (!rom.hasMoveTutors()) {
            enumOff(s, off, "MoveTutorMovesMod", Settings.MoveTutorMovesMod.UNCHANGED);
            enumOff(s, off, "MoveTutorsCompatibilityMod", Settings.MoveTutorsCompatibilityMod.UNCHANGED);
            flagsOff(s, off, "KeepFieldMoveTutors", "TutorLevelUpMoveSanity", "TutorFollowEvolutions",
                "TutorsForceGoodDamaging", "BlockBrokenTutorMoves");
        }
        if (!rom.hasShopRandomization()) {
            enumOff(s, off, "ShopItemsMod", Settings.ShopItemsMod.UNCHANGED);
            flagsOff(s, off, "BanBadRandomShopItems", "BanRegularShopItems", "BanOPShopItems", "BalanceShopPrices",
                "GuaranteeEvolutionItems", "GuaranteeXItems");
        }
        if (rom.generationOfPokemon() < 3) { // the GUI offers extra trainer Pokémon and better movesets from Gen 3 on; Gen 1/2 crash
            flagsOff(s, off, "BetterTrainerMovesets");
            for (String count : new String[] {"AdditionalBossTrainerPokemon", "AdditionalImportantTrainerPokemon", "AdditionalRegularTrainerPokemon"}) {
                if ((Integer) Settings.class.getMethod("get" + count).invoke(s) > 0) {
                    Settings.class.getMethod("set" + count, int.class).invoke(s, 0);
                    off.add(count);
                }
            }
        }
        if (rom.generationOfPokemon() != 7) { // Totem / ally / aura only exist in Sun/Moon/USUM
            enumOff(s, off, "TotemPokemonMod", Settings.TotemPokemonMod.UNCHANGED);
            enumOff(s, off, "AllyPokemonMod", Settings.AllyPokemonMod.UNCHANGED);
            enumOff(s, off, "AuraMod", Settings.AuraMod.UNCHANGED);
            flagsOff(s, off, "AllowTotemAltFormes", "TotemLevelsModified", "RandomizeTotemHeldItems");
        }
        if (off.isEmpty()) return false;
        System.out.println("Hinweis: Diese ROM (" + rom.getROMName() + ") unterstuetzt nicht: " + off
            + " -> deaktiviert (wie in der GUI).");
        return true;
    }

    static void flagsOff(Settings s, List<String> off, String... flags) throws Exception {
        for (String flag : flags) {
            if ((Boolean) Settings.class.getMethod("is" + flag).invoke(s)) {
                Settings.class.getMethod("set" + flag, boolean.class).invoke(s, false);
                off.add(flag);
            }
        }
    }

    static void enumOff(Settings s, List<String> off, String prop, Enum<?> unchanged) throws Exception {
        if (Settings.class.getMethod("get" + prop).invoke(s) != unchanged) {
            setEnum(s, prop, unchanged);
            off.add(prop);
        }
    }
}
