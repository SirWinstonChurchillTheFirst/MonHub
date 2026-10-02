# The randomizer presets MonHub ships (presets\*.rnqs). Randomizer_Nuzlock_with_Items is the hand-made base (the default
# on a first start); the others are written from it through the randomizer's own Settings class (RandoHelper
# settings-write), so they stay valid .rnqs files. Run after RandoHelper.jar is built:  pwsh tools\build_presets.ps1
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$hubSrc = Split-Path $PSScriptRoot -Parent
$root = Split-Path $hubSrc -Parent
# the randomizer's .jar next to the source folders, found by its checksum (see installer\build.ps1)
$jar = Get-ChildItem $root -Filter *.jar | Where-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash -eq "380DC1E6C704A9A4ED8433E8B7892A149390F8912B9107A2A0E7263CFD71C7D8" } |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $jar) { throw "Randomizer-.jar (Universal Pokemon Randomizer ZX 4.6.1) fehlt in $root." }
$cp = (Join-Path $hubSrc "java\RandoHelper.jar") + ";" + $jar
$presets = Join-Path $hubSrc "presets"
$base = Join-Path $presets "Randomizer_Nuzlock_with_Items.rnqs"

$variants = [ordered]@{
    # easy: only the Pokémon change – trainers, items and TMs stay as in the original, catching and evolving is easier
    "Randomizer_Easy_Wild_and_Starters" = @(
        "StartersMod=RANDOM_WITH_TWO_EVOLUTIONS", "WildPokemonMod=AREA_MAPPING", "WildPokemonRestrictionMod=SIMILAR_STRENGTH",
        "StaticPokemonMod=RANDOM_MATCHING", "TrainersMod=UNCHANGED", "FieldItemsMod=UNCHANGED", "ShopItemsMod=UNCHANGED",
        "PickupItemsMod=UNCHANGED", "TmsMod=UNCHANGED", "InGameTradesMod=UNCHANGED", "RandomizeWildPokemonHeldItems=false",
        "MakeEvolutionsEasier=true", "UseMinimumCatchRate=true", "MinimumCatchRateLevel=2")
    # hard: like the default, but the trainers fight back – stronger, fully evolved later on, better moves, one more Pokémon for bosses
    "Randomizer_Hard_Trainers" = @(
        "TrainersUsePokemonOfSimilarStrength=true", "TrainersLevelModified=true", "TrainersLevelModifier=20",
        "TrainersForceFullyEvolved=true", "TrainersForceFullyEvolvedLevel=40", "BetterTrainerMovesets=true",
        "AdditionalBossTrainerPokemon=1", "AdditionalImportantTrainerPokemon=1", "TrainersBlockEarlyWonderGuard=true",
        "RivalCarriesStarterThroughout=true")
    # chaos: the Pokémon themselves change – types, abilities, stats, moves and evolutions
    "Randomizer_Chaos_Everything" = @(
        "TypesMod=RANDOM_FOLLOW_EVOLUTIONS", "AbilitiesMod=RANDOMIZE", "AbilitiesFollowEvolutions=true", "BanTrappingAbilities=true",
        "BanNegativeAbilities=true", "BanBadAbilities=true", "BaseStatisticsMod=SHUFFLE", "BaseStatsFollowEvolutions=true",
        "MovesetsMod=RANDOM_PREFER_SAME_TYPE", "StartWithGuaranteedMoves=true", "GuaranteedMoveCount=4",
        "MovesetsForceGoodDamaging=true", "MovesetsGoodDamagingPercent=40", "EvolutionsMod=RANDOM", "EvosSimilarStrength=true",
        "EvosMaxThreeStages=true", "TmsHmsCompatibilityMod=RANDOM_PREFER_TYPE", "TmsFollowEvolutions=true",
        "MoveTutorMovesMod=RANDOM", "MoveTutorsCompatibilityMod=RANDOM_PREFER_TYPE", "RandomizeTrainerNames=true")
}
foreach ($name in $variants.Keys) {
    java -cp $cp RandoHelper settings-write $base (Join-Path $presets "$name.rnqs") @($variants[$name])
    Write-Host "$name.rnqs"
}
