Rare-Candy-Codes im Emulator prüfen (py-desmume + PKHeX.Core)
=============================================================

Idee: Jede ROM wird ohne Fenster in DeSmuME gestartet, mit einem Spielstand, in dessen Medizin-Tasche
Markierungs-Items liegen (Trank x123, Supertrank x45, Hypertrank x89). Der Emulator drückt sich per
Tasten bis ins Spiel. Dann wird die Markierung im RAM gesucht und geprüft, ob die Pointer-Kette des
Cheat-Codes genau darauf zeigt (= Medizin-Tasche, Platz 1).

Voraussetzungen:  pip install py-desmume      (Python 3.11)
                  .NET 10 SDK (für savegen, nutzt das NuGet-Paket PKHeX.Core)

Ablauf:
 1. ROM-Kopien nach roms/<SPIELCODE>_v<VERSION>.nds legen (z. B. roms/CPUD_v0.nds).
 2. savegen: Markierungs-Spielstände nach saves/marker_<Edition>.sav erzeugen
    (Gen 5 braucht einen echten Spielstand als Vorlage, leere Stände erkennt das Spiel nicht).
 3. python labrun.py            -> prüft alle ROMs, zeigt "works" (Code passt) und "found" (gefundene Pointer)
    INGAME=1 python labrun.py   -> Gen 4 zusätzlich erst nach "Weiter" ins Spiel prüfen
 4. Schwarz/Weiß ohne Spielstand: python newgame.py roms/IRBD_v0.nds IRBD 20000
    -> neues Spiel, danach wird der Trainername an der vom Code vorhergesagten Stelle gelesen.
