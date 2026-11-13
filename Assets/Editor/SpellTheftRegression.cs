using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class SpellTheftRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Checks/Thief interruption")]
    public static void Run()
    {
        var game = new SpellGame();
        game.StartGame(withThief: true);
        SpellGame.Enemy thief = null, other = null;
        foreach (var enemy in game.Enemies)
            if (enemy.IsThief) thief = enemy; else other = enemy;
        Require(thief != null && other != null, "Thief encounter missing");

        // Seed two placements to isolate interruption from random starting shapes.
        var pieces = (List<SpellGame.Placement>)typeof(SpellGame.Board)
            .GetField("_pieces", Private).GetValue(game.Boards[0]);
        var first = new SpellGame.Placement();
        var second = new SpellGame.Placement();
        typeof(SpellGame.Placement).GetProperty("Card").SetValue(first, game.Hand[0]);
        typeof(SpellGame.Placement).GetProperty("Card").SetValue(second, game.Hand[1]);
        pieces.Add(first); pieces.Add(second);
        Require(game.BeginTheft(), "Approach did not begin");
        Hit(game, other, 1);
        Require(game.HasActiveTheft, "Other enemy damage interrupted theft");
        typeof(SpellGame.Enemy).GetProperty("Block").SetValue(thief, 5);
        Hit(game, thief, 5);
        Require(game.HasActiveTheft, "Fully blocked hit interrupted theft");

        // Every spell consumes its cards through Discard, including defense spells.
        var consumed = game.TheftTarget;
        typeof(SpellGame).GetMethod("Discard", Private).Invoke(game, new object[] { consumed });
        pieces.RemoveAll(piece => piece.Card == consumed);
        Require(game.TryGetTheftPlacement(out _, out var target) && target.Card != consumed,
            "Consumed target did not retarget");
        pieces.Clear();
        Require(!game.TryGetTheftPlacement(out _, out _) && !game.HasActiveTheft,
            "Empty board did not cancel the approach");
        pieces.Add(first);
        Require(game.BeginTheft(), "Next approach did not begin");
        Hit(game, thief, 1);
        Require(!game.HasActiveTheft, "Thief HP damage did not interrupt theft");
        Require(game.BeginTheft(), "Next approach did not begin");
        game.CancelTheft();
        Require(!game.HasActiveTheft, "Explicit interruption failed");
        Debug.Log("SpellTheftRegression PASS");
    }

    private static void Hit(SpellGame game, SpellGame.Enemy enemy, int damage)
        => typeof(SpellGame).GetMethod("DamageEnemy", Private).Invoke(game, new object[] { enemy, damage });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
