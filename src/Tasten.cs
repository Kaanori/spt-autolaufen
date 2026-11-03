// ---------------------------------------------------------------
//  Tastenlisten
//
//  Drei Listen steuern das Plugin: Startertasten, Doppeltipptasten
//  und Abbruchtasten. Alle drei stehen als Text in der
//  Einstellungsdatei ("W, A, S, D, Space") und werden hier in
//  echte Tasten uebersetzt.
//
//  WARUM NICHT BepInEx' KeyboardShortcut: erstens speichert der
//  nur EINE Kombination, wir brauchen aber Listen. Zweitens
//  verlangt sein ModifierKeyTest(), dass ausser der Kombination
//  KEINE weitere Taste gedrueckt ist - wer beim Tippen Shift oder
//  die Strafetaste haelt, loest damit nichts aus. Beim Laufen ist
//  das genau der Normalfall.
//
//  Hier wird deshalb nur geprueft, was die Kombination wirklich
//  fordert: Zusatztasten gehalten, Haupttaste neu gedrueckt.
//  Alles andere darf gleichzeitig passieren.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AutoLaufen
{
	// Eine Taste, wahlweise mit Zusatztasten ("LeftControl+W").
	internal struct Tastenkombi
	{
		internal KeyCode Haupt;

		internal KeyCode[] Zusatz;

		internal Tastenkombi(KeyCode haupt, KeyCode[] zusatz)
		{
			Haupt = haupt;
			Zusatz = zusatz ?? new KeyCode[0];
		}

		// Alle Zusatztasten gehalten und die Haupttaste NEU gedrueckt.
		internal bool NeuGedrueckt()
		{
			if (Haupt == KeyCode.None)
			{
				return false;
			}
			for (int i = 0; i < Zusatz.Length; i++)
			{
				if (!Input.GetKey(Zusatz[i]))
				{
					return false;
				}
			}
			return Input.GetKeyDown(Haupt);
		}

		// Dasselbe, aber "wird gerade gehalten".
		internal bool Gehalten()
		{
			if (Haupt == KeyCode.None)
			{
				return false;
			}
			for (int i = 0; i < Zusatz.Length; i++)
			{
				if (!Input.GetKey(Zusatz[i]))
				{
					return false;
				}
			}
			return Input.GetKey(Haupt);
		}

		public override string ToString()
		{
			if (Zusatz.Length == 0)
			{
				return Haupt.ToString();
			}
			StringBuilder bau = new StringBuilder();
			for (int i = 0; i < Zusatz.Length; i++)
			{
				bau.Append(Zusatz[i].ToString());
				bau.Append('+');
			}
			bau.Append(Haupt.ToString());
			return bau.ToString();
		}

		// Lesbarer Name fuer die Anzeige im F12-Menue.
		internal string Anzeige()
		{
			string text = ToString();
			switch (Haupt)
			{
			case KeyCode.Mouse0:
				return text + "   (Maus links)";
			case KeyCode.Mouse1:
				return text + "   (Maus rechts)";
			case KeyCode.Mouse2:
				return text + "   (Mausrad-Klick)";
			case KeyCode.Mouse3:
				return text + "   (Maus Seitentaste 1)";
			case KeyCode.Mouse4:
				return text + "   (Maus Seitentaste 2)";
			case KeyCode.Space:
				return text + "   (Leertaste)";
			default:
				return text;
			}
		}
	}

	internal static class Tastenliste
	{
		// Alles von Backspace bis Mouse6. Darueber faengt der
		// Joystick-Wust an (Joystick1Button0 und so weiter), den will
		// hier niemand versehentlich zugewiesen bekommen.
		private const int HoechsterCode = 329;

		private static KeyCode[] _alleTasten;

		internal static readonly KeyCode[] Zusatztasten = new KeyCode[11]
		{
			KeyCode.LeftControl, KeyCode.RightControl,
			KeyCode.LeftShift, KeyCode.RightShift,
			KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.AltGr,
			KeyCode.LeftCommand, KeyCode.RightCommand,
			KeyCode.LeftWindows, KeyCode.RightWindows
		};

		// Alle Tasten, die man zuweisen darf - Tastatur und Maus.
		internal static KeyCode[] Alle()
		{
			if (_alleTasten != null)
			{
				return _alleTasten;
			}
			List<KeyCode> sammlung = new List<KeyCode>();
			foreach (KeyCode wert in Enum.GetValues(typeof(KeyCode)))
			{
				int zahl = (int)wert;
				if (zahl > 0 && zahl <= HoechsterCode && !sammlung.Contains(wert))
				{
					sammlung.Add(wert);
				}
			}
			_alleTasten = sammlung.ToArray();
			return _alleTasten;
		}

		internal static bool IstZusatztaste(KeyCode taste)
		{
			for (int i = 0; i < Zusatztasten.Length; i++)
			{
				if (Zusatztasten[i] == taste)
				{
					return true;
				}
			}
			return false;
		}

		// "W, A, LeftControl+D" -> drei Eintraege.
		// Unbekannte Namen werden uebergangen und gemeldet.
		internal static List<Tastenkombi> Lesen(string text, Action<string> warnen)
		{
			List<Tastenkombi> ergebnis = new List<Tastenkombi>();
			if (string.IsNullOrEmpty(text))
			{
				return ergebnis;
			}
			string[] eintraege = text.Split(new char[3] { ',', ';', '\n' },
				StringSplitOptions.RemoveEmptyEntries);
			foreach (string eintrag in eintraege)
			{
				string[] teile = eintrag.Split(new char[1] { '+' },
					StringSplitOptions.RemoveEmptyEntries);
				List<KeyCode> zusatz = new List<KeyCode>();
				KeyCode haupt = KeyCode.None;
				bool heil = true;
				for (int i = 0; i < teile.Length; i++)
				{
					string name = teile[i].Trim();
					if (name.Length == 0)
					{
						continue;
					}
					KeyCode taste;
					if (!Enum.TryParse<KeyCode>(name, ignoreCase: true, result: out taste)
						|| !Enum.IsDefined(typeof(KeyCode), taste) || taste == KeyCode.None)
					{
						if (warnen != null)
						{
							warnen(name);
						}
						heil = false;
						break;
					}
					// Der letzte Teil ist die Haupttaste, alles davor
					// sind Zusatztasten.
					if (i == teile.Length - 1)
					{
						haupt = taste;
					}
					else
					{
						zusatz.Add(taste);
					}
				}
				if (heil && haupt != KeyCode.None)
				{
					ergebnis.Add(new Tastenkombi(haupt, zusatz.ToArray()));
				}
			}
			return ergebnis;
		}

		// Zurueck in Text, so wie er in der Einstellungsdatei steht.
		internal static string Schreiben(List<Tastenkombi> liste)
		{
			StringBuilder bau = new StringBuilder();
			for (int i = 0; i < liste.Count; i++)
			{
				if (i > 0)
				{
					bau.Append(", ");
				}
				bau.Append(liste[i].ToString());
			}
			return bau.ToString();
		}
	}

	// ---------------------------------------------------------------
	//  Erkennungszeichen fuer den ConfigurationManager (das F12-Menue).
	//
	//  Der liest diese Klasse per Reflexion aus und ordnet die Felder
	//  ueber ihren NAMEN zu - die Klasse muss deshalb genau so heissen
	//  und die Felder auch. Ein fester Verweis auf den
	//  ConfigurationManager waere das Gegenteil von gut: fehlt er,
	//  wuerde das Plugin gar nicht erst laden. So bleibt er optional.
	// ---------------------------------------------------------------
	internal sealed class ConfigurationManagerAttributes
	{
		public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;
	}
}
