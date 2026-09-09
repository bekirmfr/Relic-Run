using System;
using System.Collections.Generic;
using RelicRun.Core.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// A screen that is a heading and a list of lines.
    /// </summary>
    /// <remarks>
    /// Four of the game's screens are this and nothing else: the board, the profile, the
    /// bestiary and the relic book. They differ in what the lines SAY and in nothing else, so
    /// they share the machinery and each supplies only its own words.
    ///
    /// One line per row rather than a row of aligned columns. That is a scaffold's answer and it
    /// is deliberate — a column layout is where a list screen's real work is, and doing it four
    /// times before any of them has been seen would be four guesses. The fight's log is built the
    /// same way for the same reason.
    /// </remarks>
    public abstract class SheetPanel : MetaPanel
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _note;
        [SerializeField] private RectTransform _list;
        [SerializeField] private TMP_Text _row;
        [SerializeField] private Button _back;

        /// <summary>The ink of a row that is nothing special.</summary>
        protected static readonly Color Plain = new Color(0.90f, 0.87f, 0.80f);

        /// <summary>And of one that is dimmed: locked, unmet, unearned.</summary>
        protected static readonly Color Faint = new Color(0.42f, 0.40f, 0.35f);

        /// <summary>The delver's own, or anything the screen is pointing at.</summary>
        protected static readonly Color Lit = new Color(0.89f, 0.70f, 0.25f);

        private readonly List<TMP_Text> _rows = new List<TMP_Text>();

        /// <summary>One line, and what colour it is.</summary>
        protected struct Row
        {
            public string Text;
            public Color Ink;

            public Row(string text, Color ink)
            {
                Text = text;
                Ink = ink;
            }
        }

        private void Awake()
        {
            if (_back != null) _back.onClick.AddListener(() => Go(Page.Title));
        }

        /// <summary>Puts a heading, a note under it, and a list on the screen.</summary>
        protected void Sheet(string title, string note, IReadOnlyList<Row> rows)
        {
            if (_title != null) _title.text = title ?? string.Empty;

            if (_note != null)
            {
                _note.text = note ?? string.Empty;
                _note.color = Faint;
            }

            Lines(rows);
        }

        /// <summary>
        /// The rows, spawned once and refilled after.
        /// </summary>
        /// <remarks>
        /// Grown to fit and never shrunk, because these lists are read repeatedly and their
        /// length barely moves — the relic book is fifty rows every time it opens. Spare rows are
        /// hidden rather than destroyed, so reopening a screen costs nothing.
        /// </remarks>
        private void Lines(IReadOnlyList<Row> rows)
        {
            if (_row == null || _list == null) return;

            while (_rows.Count < rows.Count)
            {
                TMP_Text made = Instantiate(_row, _list);

                made.gameObject.SetActive(true);
                _rows.Add(made);
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                bool used = i < rows.Count;

                _rows[i].gameObject.SetActive(used);

                if (!used) continue;

                _rows[i].text = rows[i].Text;
                _rows[i].color = rows[i].Ink;
            }
        }

        /// <summary>A word from the delver's own language, or the key when there is none.</summary>
        /// <remarks>
        /// Every one of these screens is translated, so nothing on them is spelled here. A
        /// missing locale answers with the key, which is ugly on screen and diagnosable — the
        /// alternative is a blank, which is neither.
        /// </remarks>
        protected string Say(string key)
        {
            return Words != null ? Words.Get(key) : key;
        }

        /// <summary>The same, with its placeholders filled.</summary>
        protected string Say(string key, IReadOnlyDictionary<string, string> values)
        {
            return Words != null ? Words.Get(key, values) : key;
        }

        /// <summary>A one-off dictionary for a string with placeholders in it.</summary>
        protected static Dictionary<string, string> With(string name, string value)
        {
            return new Dictionary<string, string> { { name, value } };
        }

        /// <summary>And for two, which is as many as any of these strings takes.</summary>
        protected static Dictionary<string, string> With(string first, string one,
            string second, string two)
        {
            return new Dictionary<string, string> { { first, one }, { second, two } };
        }
    }
}
