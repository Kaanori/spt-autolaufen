// ---------------------------------------------------------------
//  Auto-Laufen
//
//  Zwei schnelle Tipps auf W - und die Spielfigur laeuft weiter
//  vorwaerts, als bliebe W gedrueckt. Ein Druck auf eine der
//  Abbruchtasten (ab Werk W, A, S, D, Leertaste) beendet ihn
//  sofort. Maus und alle anderen Tasten stoeren nicht.
//
//  WO DER HEBEL SITZT: Tastatur und Maus landen in EFT nicht
//  direkt bei der Spielfigur. Der InputManager sammelt sie jeden
//  Frame in ein float[] mit sieben Achsen (EAxis: MoveX, MoveY,
//  TurnX, TurnY, LookX, LookY, LeanX) und schickt dieses Feld
//  durch
//
//      GamePlayerOwner.TranslateAxes(ref float[] axes)
//        -> PlayerOwner.TranslateAxes
//          -> _moveInputTranslator.TranslateAxes
//            -> Player.Move(Vector2)
//
//  axes[1] ist MoveY: +1 bedeutet "W gedrueckt", -1 "S gedrueckt".
//  Wir haengen uns mit einem Prefix VOR diese Methode und tragen
//  dort eine 1 ein. Von da an ist der Rest des Spiels ahnungslos -
//  Ausdauer, Schrittgeraeusche, Animation, Traegheit, alles laeuft
//  wie bei echtem Tastendruck. Weil nur axes[1] angefasst wird,
//  bleiben Maus, Umsehen, Lehnen und Strafen unberuehrt.
//
//  ABSICHTLICH NICHT Player.Move() GEPATCHT: das ist zwar die
//  offensichtlichere Stelle, sitzt aber HINTER dem Uebersetzer.
//  Die Geschwindigkeitsstufe (Mausrad), der Wiegeschritt und die
//  Vorwaerts-Pruefung IsForwardInputDirection werden dort schon
//  aus der alten Richtung berechnet - das Ergebnis waere eine
//  Figur, die laeuft, deren Zustandsautomat das aber nicht
//  mitbekommt.
//
//  DREI ABBRUCHWEGE, weil ein Dauerlauf, den man nicht mehr los
//  wird, gefaehrlicher ist als keiner:
//   1. Jede Taste aus der Abbruchliste.
//   2. Eine Startertaste nochmal.
//   3. Tod, Raid-Ende oder Verlassen der Karte.
//
//  Einstellbar unter F12 -> Auto-Laufen, dort auch die Tasten
//  ueber "+ Taste hinzufuegen".
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace AutoLaufen
{
	// Wie die Startertasten wirken sollen.
	public enum EBetriebsart
	{
		// Einmal druecken = an, nochmal druecken = aus.
		Umschalten,
		// Laeuft nur, solange die Taste gehalten wird.
		Halten
	}

	[BepInPlugin("com.kaan.autolaufen", "Auto-Laufen", "1.2.0")]
	public class Plugin : BaseUnityPlugin
	{
		internal static ManualLogSource Log;

		internal static ConfigEntry<bool> An;

		internal static ConfigEntry<bool> ImInventarWeiterlaufen;

		internal static ConfigEntry<string> Startertasten;

		internal static ConfigEntry<bool> DoppeltippAn;

		internal static ConfigEntry<string> Doppeltipptasten;

		internal static ConfigEntry<float> DoppeltippFenster;

		internal static ConfigEntry<EBetriebsart> Betriebsart;

		internal static ConfigEntry<string> Abbruchtasten;

		internal static ConfigEntry<bool> Sprinten;

		internal static ConfigEntry<bool> HinweisZeigen;

		internal static ConfigEntry<bool> Protokoll;

		// Laeuft der Dauerlauf gerade? Der Harmony-Patch liest das
		// Feld, dieses Skript schreibt es.
		internal static bool Laeuft;

		// Die drei uebersetzten Listen, dazu der Text, aus dem sie
		// entstanden. Neu uebersetzt wird nur bei echter Aenderung -
		// das F12-Menue kann den Text jederzeit umschreiben.
		private static readonly Dictionary<ConfigEntry<string>, string> _rohtexte =
			new Dictionary<ConfigEntry<string>, string>();

		private static readonly Dictionary<ConfigEntry<string>, List<Tastenkombi>> _listen =
			new Dictionary<ConfigEntry<string>, List<Tastenkombi>>();

		// Zeitpunkt und Nummer des letzten Tipps auf eine
		// Doppeltipptaste. Die Nummer muss stimmen, sonst wuerde
		// "W dann Pfeil hoch" als Doppeltipp durchgehen.
		private static float _letzterTipp = -999f;

		private static int _letzterTippNr = -1;

		// Wurde der Lauf in DIESEM Frame eingeschaltet? Dann darf die
		// Abbruchpruefung ihn nicht gleich wieder abwuergen - der
		// zweite Tipp auf W ist ja selbst eine Abbruchtaste.
		private static bool _geradeEingeschaltet;

		// --- Zustand der Tastenaufnahme im F12-Menue ---

		// Welche Liste wartet gerade auf eine Taste? null = keine.
		private static ConfigEntry<string> _nimmtAuf;

		// Nach dem Klick auf "+" ist die linke Maustaste noch unten.
		// Erst wenn alles losgelassen ist, hoeren wir wieder zu -
		// sonst traegt sich sofort Mouse0 selbst ein.
		private static bool _warteAufLoslassen;

		// Aus dem Zeichnen heraus darf die Liste nicht veraendert
		// werden: GUILayout zeichnet jeden Frame zweimal und beschwert
		// sich, wenn dazwischen Knoepfe verschwinden. Wuensche werden
		// deshalb gemerkt und im naechsten Update ausgefuehrt.
		private static ConfigEntry<string> _wunschListe;

		private static int _wunschEntfernen = -1;

		private static bool _wunschAufnehmen;

		private GUIStyle _stil;

		private void Awake()
		{
			Log = base.Logger;

			An = base.Config.Bind("1. Allgemein", "Eingeschaltet", defaultValue: true,
				"Aus: Das Plugin bleibt still, keine Taste tut etwas.");

			ImInventarWeiterlaufen = base.Config.Bind("1. Allgemein",
				"Weiterlaufen mit offenem Inventar", defaultValue: true,
				"An: Der Dauerlauf laeuft weiter, waehrend das Inventar offen\n"
				+ "ist. Gilt nur fuer den Dauerlauf - ohne ihn bleibt alles beim\n"
				+ "Alten, das Inventar haelt dich wie immer an.\n"
				+ "Gelenkt wird dabei nicht: die Maus gehoert dem Inventar.");

			Startertasten = base.Config.Bind("2. Starten", "Startertasten", "Home",
				new ConfigDescription(
					"Jede dieser Tasten schaltet den Dauerlauf an und aus.\n"
					+ "Ueber \"+ Taste hinzufuegen\" nimmst du eine neue auf -\n"
					+ "Tastatur oder Maustaste. Haeltst du beim Aufnehmen Strg,\n"
					+ "Alt oder Shift, wird daraus eine Kombination.\n"
					+ "Ab Werk Pos1 (Home), die belegt Tarkov selbst nicht.",
					null,
					new ConfigurationManagerAttributes { CustomDrawer = ListeZeichnen }));

			DoppeltippAn = base.Config.Bind("2. Starten", "Doppeltipp verwenden", defaultValue: true,
				"An: Zwei schnelle Tipps auf eine Doppeltipptaste starten den\n"
				+ "Dauerlauf. Aus: Nur die Startertasten starten ihn.");

			Doppeltipptasten = base.Config.Bind("2. Starten", "Doppeltipptasten", "W",
				new ConfigDescription(
					"Diese Tasten starten den Dauerlauf, wenn du sie zweimal\n"
					+ "schnell hintereinander tippst. Ab Werk W - also die Taste,\n"
					+ "mit der du ohnehin vorwaerts gehst.",
					null,
					new ConfigurationManagerAttributes { CustomDrawer = ListeZeichnen }));

			DoppeltippFenster = base.Config.Bind("2. Starten", "Doppeltipp-Zeitfenster", 0.35f,
				new ConfigDescription(
					"Wie viele Sekunden die beiden Tipps hoechstens auseinander\n"
					+ "liegen duerfen. Kleiner = du musst schneller tippen, loest\n"
					+ "aber seltener versehentlich aus. Groesser = umgekehrt.",
					new AcceptableValueRange<float>(0.1f, 1f)));

			Betriebsart = base.Config.Bind("2. Starten", "Betriebsart der Startertasten",
				EBetriebsart.Umschalten,
				"Umschalten: einmal druecken = laeuft, nochmal druecken = Stopp.\n"
				+ "Halten: laeuft nur, solange die Taste unten ist. Der Doppeltipp\n"
				+ "arbeitet immer als Umschalter, unabhaengig davon.");

			Abbruchtasten = base.Config.Bind("3. Stoppen", "Abbruchtasten", "W, A, S, D, Space",
				new ConfigDescription(
					"Diese Tasten beenden den Dauerlauf sofort. ALLES, was nicht\n"
					+ "in der Liste steht, stoert ihn nicht - Maus, Umsehen,\n"
					+ "Nachladen, Buecken, Waffenwechsel laufen ungestoert weiter.\n"
					+ "Ueber \"+ Taste hinzufuegen\" aufnehmen, ueber X entfernen.\n"
					+ "Leere Liste = nur die Startertasten beenden den Lauf.",
					null,
					new ConfigurationManagerAttributes { CustomDrawer = ListeZeichnen }));

			Sprinten = base.Config.Bind("4. Verhalten", "Dauerhaft sprinten", defaultValue: false,
				"An: Die Figur sprintet, statt zu gehen. Achtung, das leert die\n"
				+ "Ausdauer; ist sie leer, faellt das Spiel auf Gehen zurueck und\n"
				+ "das Plugin schaltet den Sprint wieder ein, sobald es geht -\n"
				+ "das ruckelt dann merklich. Ab Werk deshalb aus.");

			HinweisZeigen = base.Config.Bind("5. Anzeige", "Hinweis einblenden", defaultValue: true,
				"Blendet oben links \"Dauerlauf\" ein, solange er laeuft. Ohne den\n"
				+ "Hinweis vergisst man leicht, dass er noch an ist.");

			Protokoll = base.Config.Bind("6. Diagnose", "Protokoll", defaultValue: false,
				"Schreibt jedes An und Aus samt Ausloeser in die BepInEx-Konsole.\n"
				+ "Hilfreich, wenn der Lauf zu frueh abbricht und du wissen\n"
				+ "willst, welche Taste schuld war.");

			try
			{
				new Harmony("com.kaan.autolaufen").PatchAll();
				Log.LogInfo("Auto-Laufen: bereit.");
			}
			catch (Exception ex)
			{
				Log.LogError("Auto-Laufen: Patch fehlgeschlagen - " + ex);
			}
		}

		// ---------------------------------------------------------
		//  Listen
		// ---------------------------------------------------------

		// Uebersetzte Liste zu einer Einstellung. Uebersetzt nur neu,
		// wenn sich der Text geaendert hat.
		internal static List<Tastenkombi> Liste(ConfigEntry<string> eintrag)
		{
			string roh = eintrag.Value ?? string.Empty;
			string alt;
			if (_rohtexte.TryGetValue(eintrag, out alt) && alt == roh)
			{
				return _listen[eintrag];
			}
			List<Tastenkombi> neu = Tastenliste.Lesen(roh, delegate(string name)
			{
				Log.LogWarning("Auto-Laufen: \"" + name + "\" in \"" + eintrag.Definition.Key
					+ "\" kenne ich nicht - Eintrag uebergangen.");
			});
			_rohtexte[eintrag] = roh;
			_listen[eintrag] = neu;
			return neu;
		}

		// ---------------------------------------------------------
		//  Anzeige im F12-Menue
		// ---------------------------------------------------------

		private static void ListeZeichnen(ConfigEntryBase roh)
		{
			ConfigEntry<string> eintrag = roh as ConfigEntry<string>;
			if (eintrag == null)
			{
				return;
			}
			try
			{
				List<Tastenkombi> liste = Liste(eintrag);

				GUILayout.BeginVertical();

				if (liste.Count == 0)
				{
					GUILayout.Label("(keine Taste)");
				}
				for (int i = 0; i < liste.Count; i++)
				{
					GUILayout.BeginHorizontal();
					GUILayout.Label(liste[i].Anzeige(), GUILayout.MinWidth(170f));
					if (GUILayout.Button("X", GUILayout.Width(26f)))
					{
						_wunschListe = eintrag;
						_wunschEntfernen = i;
					}
					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
				}

				if (_nimmtAuf == eintrag)
				{
					GUILayout.Label("Druecke jetzt eine Taste oder Maustaste ...   "
						+ "(Esc bricht ab)");
				}
				else if (GUILayout.Button("+ Taste hinzufuegen"))
				{
					_wunschListe = eintrag;
					_wunschAufnehmen = true;
				}

				GUILayout.EndVertical();
			}
			catch (Exception ex)
			{
				Log.LogError("Auto-Laufen: Fehler beim Zeichnen der Liste - " + ex);
			}
		}

		// Die im Zeichnen gemerkten Wuensche ausfuehren.
		private static void WuenscheAusfuehren()
		{
			if (_wunschListe == null)
			{
				return;
			}
			ConfigEntry<string> eintrag = _wunschListe;
			_wunschListe = null;

			if (_wunschAufnehmen)
			{
				_wunschAufnehmen = false;
				_nimmtAuf = eintrag;
				_warteAufLoslassen = true;
				return;
			}

			if (_wunschEntfernen >= 0)
			{
				int nummer = _wunschEntfernen;
				_wunschEntfernen = -1;
				List<Tastenkombi> liste = new List<Tastenkombi>(Liste(eintrag));
				if (nummer < liste.Count)
				{
					if (Protokoll.Value)
					{
						Log.LogInfo("Auto-Laufen: " + liste[nummer] + " aus \""
							+ eintrag.Definition.Key + "\" entfernt.");
					}
					liste.RemoveAt(nummer);
					eintrag.Value = Tastenliste.Schreiben(liste);
				}
			}
		}

		// Auf die naechste gedrueckte Taste warten und sie eintragen.
		private static void AufnahmeSchritt()
		{
			ConfigEntry<string> eintrag = _nimmtAuf;

			// Erst alles loslassen. Sonst traegt sich der Klick auf
			// "+ Taste hinzufuegen" selbst als Mouse0 ein.
			if (_warteAufLoslassen)
			{
				if (!Input.anyKey)
				{
					_warteAufLoslassen = false;
				}
				return;
			}

			if (Input.GetKeyDown(KeyCode.Escape))
			{
				_nimmtAuf = null;
				return;
			}

			KeyCode[] alle = Tastenliste.Alle();
			for (int i = 0; i < alle.Length; i++)
			{
				KeyCode taste = alle[i];
				if (!Input.GetKeyDown(taste))
				{
					continue;
				}

				// Eine Zusatztaste allein darf auch zugewiesen werden -
				// dann aber ohne weitere Zusatztasten davor.
				List<KeyCode> zusatz = new List<KeyCode>();
				if (!Tastenliste.IstZusatztaste(taste))
				{
					for (int z = 0; z < Tastenliste.Zusatztasten.Length; z++)
					{
						if (Input.GetKey(Tastenliste.Zusatztasten[z]))
						{
							zusatz.Add(Tastenliste.Zusatztasten[z]);
						}
					}
				}

				Tastenkombi neu = new Tastenkombi(taste, zusatz.ToArray());
				List<Tastenkombi> liste = new List<Tastenkombi>(Liste(eintrag));
				string text = neu.ToString();
				bool schonDrin = false;
				for (int p = 0; p < liste.Count; p++)
				{
					if (liste[p].ToString() == text)
					{
						schonDrin = true;
						break;
					}
				}
				if (!schonDrin)
				{
					liste.Add(neu);
					eintrag.Value = Tastenliste.Schreiben(liste);
					Log.LogInfo("Auto-Laufen: " + text + " zu \"" + eintrag.Definition.Key
						+ "\" hinzugefuegt.");
				}
				_nimmtAuf = null;
				return;
			}
		}

		// ---------------------------------------------------------
		//  Zustand
		// ---------------------------------------------------------

		// Die eigene Spielfigur - oder null, wenn gerade kein Raid
		// laeuft (Versteck, Hauptmenue, Ladebildschirm).
		internal static Player Spielfigur()
		{
			if (!Singleton<GameWorld>.Instantiated)
			{
				return null;
			}
			GameWorld welt = Singleton<GameWorld>.Instance;
			if ((object)welt == null)
			{
				return null;
			}
			return welt.MainPlayer;
		}

		internal static void Anschalten(Player figur, string grund)
		{
			if (Laeuft)
			{
				return;
			}
			Laeuft = true;
			_geradeEingeschaltet = true;
			_letzterTipp = -999f;
			_letzterTippNr = -1;
			if (Protokoll.Value)
			{
				Log.LogInfo("Auto-Laufen: an (" + grund + ").");
			}
		}

		internal static void Ausschalten(Player figur, string grund)
		{
			if (!Laeuft)
			{
				return;
			}
			Laeuft = false;
			// Tipp-Verlauf loeschen. Sonst zaehlt der Druck, der eben
			// gestoppt hat, gleich als erster Tipp eines neuen
			// Doppeltipps - und der Lauf ginge sofort wieder an.
			_letzterTipp = -999f;
			_letzterTippNr = -1;
			// Den Sprint nicht anlassen, sonst rennt die Figur nach dem
			// Abschalten noch einen Moment weiter.
			try
			{
				if (Sprinten.Value && (object)figur != null && figur.IsSprintEnabled)
				{
					figur.EnableSprint(enable: false);
				}
			}
			catch (Exception ex)
			{
				Log.LogWarning("Auto-Laufen: Sprint liess sich nicht abschalten - " + ex.Message);
			}
			if (Protokoll.Value)
			{
				Log.LogInfo("Auto-Laufen: aus (" + grund + ").");
			}
		}

		private void Update()
		{
			try
			{
				WuenscheAusfuehren();

				// Tastenaufnahme geht allem vor und laeuft auch im
				// Hauptmenue - dort stellt man sie ja ein.
				if (_nimmtAuf != null)
				{
					AufnahmeSchritt();
					return;
				}

				_geradeEingeschaltet = false;

				Player figur = Spielfigur();

				// Kein Raid, tot oder Plugin aus: Zustand zuruecksetzen.
				// Sonst laeuft man im naechsten Raid sofort wieder los.
				if (!An.Value || (object)figur == null || figur.HealthController == null
					|| !figur.HealthController.IsAlive)
				{
					if (Laeuft)
					{
						Ausschalten(figur, "kein lebender Spieler im Raid");
					}
					_letzterTipp = -999f;
					_letzterTippNr = -1;
					return;
				}

				// 1) Startertasten
				List<Tastenkombi> starter = Liste(Startertasten);
				if (Betriebsart.Value == EBetriebsart.Halten)
				{
					bool gehalten = false;
					for (int i = 0; i < starter.Count; i++)
					{
						if (starter[i].Gehalten())
						{
							gehalten = true;
							break;
						}
					}
					if (gehalten && !Laeuft)
					{
						Anschalten(figur, "Startertaste gehalten");
					}
					else if (!gehalten && Laeuft)
					{
						Ausschalten(figur, "Startertaste losgelassen");
					}
				}
				else
				{
					for (int i = 0; i < starter.Count; i++)
					{
						if (!starter[i].NeuGedrueckt())
						{
							continue;
						}
						if (Laeuft)
						{
							Ausschalten(figur, "Startertaste " + starter[i]);
						}
						else
						{
							Anschalten(figur, "Startertaste " + starter[i]);
						}
						break;
					}
				}

				// 2) Doppeltipp
				if (DoppeltippAn.Value)
				{
					List<Tastenkombi> tipptasten = Liste(Doppeltipptasten);
					for (int i = 0; i < tipptasten.Count; i++)
					{
						if (!tipptasten[i].NeuGedrueckt())
						{
							continue;
						}
						float jetzt = Time.unscaledTime;
						if (!Laeuft && _letzterTippNr == i
							&& jetzt - _letzterTipp <= DoppeltippFenster.Value)
						{
							Anschalten(figur, "Doppeltipp " + tipptasten[i]);
						}
						else
						{
							// Erster Tipp - oder der Lauf ist ohnehin an.
							// Merken, damit ein schneller zweiter zaehlt.
							_letzterTipp = jetzt;
							_letzterTippNr = i;
						}
						break;
					}
				}

				// 3) Abbruchtasten. Zwei Faelle bleiben aussen vor:
				//    - Der Frame des Einschaltens. Der zweite Tipp auf W
				//      ist selbst eine Abbruchtaste und wuerde den Lauf
				//      sofort wieder abwuergen.
				//    - Betriebsart "Halten". Dort sagt die gehaltene
				//      Taste jeden Frame aufs Neue "lauf", ein Abbruch
				//      waere im naechsten Frame zurueckgenommen - ein
				//      Flackern. Dort beendet Loslassen den Lauf.
				if (Laeuft && !_geradeEingeschaltet
					&& Betriebsart.Value != EBetriebsart.Halten)
				{
					List<Tastenkombi> stopp = Liste(Abbruchtasten);
					for (int i = 0; i < stopp.Count; i++)
					{
						if (stopp[i].NeuGedrueckt())
						{
							Ausschalten(figur, "Abbruchtaste " + stopp[i]);
							break;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.LogError("Auto-Laufen: Fehler in Update - " + ex);
				Laeuft = false;
			}
		}

		private void OnGUI()
		{
			if (!Laeuft || !HinweisZeigen.Value)
			{
				return;
			}
			if (_stil == null)
			{
				_stil = new GUIStyle(GUI.skin.label)
				{
					fontSize = 18,
					fontStyle = FontStyle.Bold
				};
				_stil.normal.textColor = new Color(1f, 0.85f, 0.4f, 0.9f);
			}
			GUI.Label(new Rect(20f, 20f, 300f, 30f), "Dauerlauf", _stil);
		}
	}

	// ---------------------------------------------------------------
	//  Eingriff 1: eine 1 in die Vorwaerts-Achse.
	// ---------------------------------------------------------------
	[HarmonyPatch(typeof(GamePlayerOwner), nameof(GamePlayerOwner.TranslateAxes))]
	internal static class AchsenPatch
	{
		// EAxis.MoveY - nicht fest verdrahtet, sondern aus der
		// Aufzaehlung des Spiels gelesen. Sortiert BSG sie eines Tages
		// um, wandert der Index automatisch mit.
		internal static readonly int MoveY = (int)EFT.InputSystem.EAxis.MoveY;

		[HarmonyPrefix]
		internal static void Vorher(GamePlayerOwner __instance, ref float[] axes)
		{
			try
			{
				if (!Plugin.An.Value || !Plugin.Laeuft)
				{
					return;
				}
				if (axes == null || axes.Length <= MoveY)
				{
					return;
				}

				Player figur = __instance.Player;
				if ((object)figur == null)
				{
					return;
				}

				// Nur die eigene Figur. GamePlayerOwner gehoert zwar
				// ohnehin nur dem Spieler, aber das kostet nichts.
				Player eigene = Plugin.Spielfigur();
				if ((object)eigene != null && (object)eigene != (object)figur)
				{
					return;
				}

				if (figur.HealthController == null || !figur.HealthController.IsAlive)
				{
					Plugin.Ausschalten(figur, "Spieler nicht mehr am Leben");
					return;
				}

				// Nur die Vorwaerts-Achse. axes[0] (Strafen), axes[2..5]
				// (Maus, Umsehen) und axes[6] (Lehnen) bleiben unberuehrt -
				// darum stoert die Maus den Dauerlauf nie.
				axes[MoveY] = 1f;

				if (Plugin.Sprinten.Value && !figur.IsSprintEnabled
					&& figur.MovementContext != null && figur.MovementContext.CanSprint)
				{
					figur.EnableSprint(enable: true);
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("Auto-Laufen: Fehler im Achsen-Patch - " + ex);
				Plugin.Laeuft = false;
			}
		}
	}

	// ---------------------------------------------------------------
	//  Eingriff 2: das Inventar haelt den Dauerlauf nicht mehr an.
	//
	//  WARUM MAN IM INVENTAR STEHENBLEIBT: Die Eingabe wandert durch
	//  einen Baum von Knoten (InputNodeAbstract.TranslateInput), der
	//  von oben nach unten abgearbeitet wird - zuletzt geoeffneter
	//  Bildschirm zuerst, die Spielfigur ganz am Ende. In
	//  InputNode.TranslateInput steht:
	//
	//      if (axes != null) TranslateAxes(ref axes);
	//
	//  und EFT.UI.Screens.UIScreen.TranslateAxes besteht aus einer
	//  einzigen Zeile: axes = null. Das Feld wird per ref
	//  weitergereicht, also ist die Kette ab dort tot - der Spieler
	//  bekommt gar keine Achsen mehr und bleibt stehen.
	//
	//  Das Raid-HUD (BattleUIScreen), die Karte und die Aktionsleiste
	//  ueberschreiben TranslateAxes mit einem LEEREN Rumpf, lassen
	//  die Achsen also durch. Nur wer bei UIScreen bleibt - und das
	//  tut der InventoryScreen - blockiert.
	//
	//  UNSERE LOESUNG: statt das Feld zu loeschen, schieben wir ein
	//  EIGENES unter, in dem alles auf 0 steht ausser vorwaerts.
	//  Ergebnis: die Figur laeuft geradeaus weiter, dreht sich aber
	//  nicht und strafet nicht - die Maus gehoert dem Inventar. Das
	//  Feld des Spiels selbst wird nicht angefasst.
	// ---------------------------------------------------------------
	[HarmonyPatch(typeof(EFT.UI.Screens.UIScreen), nameof(EFT.UI.Screens.UIScreen.TranslateAxes))]
	internal static class BildschirmPatch
	{
		private static float[] _eigeneAchsen;

		// Wie viele Achsen es gibt - aus der Aufzaehlung des Spiels
		// gelesen, damit die Zahl bei einem Update mitwandert.
		private static readonly int Achsenzahl =
			Enum.GetValues(typeof(EFT.InputSystem.EAxis)).Length;

		[HarmonyPrefix]
		internal static bool Vorher(ref float[] axes)
		{
			try
			{
				if (!Plugin.An.Value || !Plugin.Laeuft
					|| !Plugin.ImInventarWeiterlaufen.Value)
				{
					// Original laeuft: axes = null, alles bleibt wie frueher.
					return true;
				}

				// Auch aus einem bereits geloeschten Feld wird wieder
				// eins. Ein verschachteltes Unterfenster kann die Achsen
				// naemlich schon vor uns auf null gesetzt haben - dann
				// waere sonst trotzdem Schluss.
				int laenge = ((axes != null) ? axes.Length : Achsenzahl);
				if (laenge <= AchsenPatch.MoveY)
				{
					return false;
				}

				if (_eigeneAchsen == null || _eigeneAchsen.Length != laenge)
				{
					_eigeneAchsen = new float[laenge];
				}
				for (int i = 0; i < _eigeneAchsen.Length; i++)
				{
					_eigeneAchsen[i] = 0f;
				}
				_eigeneAchsen[AchsenPatch.MoveY] = 1f;
				axes = _eigeneAchsen;

				// Original ueberspringen - sonst waere das Feld gleich
				// wieder null.
				return false;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("Auto-Laufen: Fehler im Bildschirm-Patch - " + ex);
				return true;
			}
		}
	}
}
