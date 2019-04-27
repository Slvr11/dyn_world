using System;
using System.Collections.Generic;
using System.Collections;
using InfinityScript;
using System.IO;//debug
using static InfinityScript.GSCFunctions;

public class dynamic_world : BaseScript
{
    private static string mapname;
    private static bool hasDoneElems = false;
    private static Dictionary<string, int> mapEffects = new Dictionary<string,int>();
    private static Entity collision;

    public dynamic_world()
    {
        mapname = GetDvar("mapname");
        Entity care_package = GetEnt("care_package", "targetname");
        collision = GetEnt(care_package.Target, "targetname");

        PlayerConnected += OnPlayerConnected;

        if (File.Exists("scripts\\entityDebugLog.txt") && !File.ReadAllText("scripts\\entityDebugLog.txt").Contains(mapname)) debugEnts();

        //initEntityList();
        initDynElems();

        if (mapname == "mp_dome")
            OnNotify("nuke_death", () =>
                Entity.GetEntity(2046).SetField("nukeDetonated", true));
        else if (mapname == "mp_bootleg")
            OnNotify("update_timelimit", (newTime) =>
            {
                Utilities.PrintToConsole("Time changed to " + newTime);
                if ((int)newTime > 0 && !Entity.GetEntity(2046).HasField("rainStarted"))
                    StartAsync(bootleg_watchForHeavyRain());
                else if ((int)newTime == 0)
                    Entity.GetEntity(2046).SetField("stopRainIncrease", true);
            });
        else if (mapname == "mp_terminal_cls")
            OnNotify("update_timelimit", (newTime) =>
            {
                Utilities.PrintToConsole("Time changed to " + newTime);
                if ((int)newTime > 0)
                    StartAsync(terminal_waitForLockdown());
            });
    }

    private void OnPlayerConnected(Entity player)
    {
        player.SpawnedPlayer += () => OnPlayerSpawned(player);
        player.NotifyOnPlayerCommand("trigger_use", "+activate");
        syncElementsWithPlayer(player);
    }

    private void OnPlayerSpawned(Entity player)
    {

    }

    public override void OnSay(Entity player, string name, string message)
    {
        if (message == "playElems")
        {
            Entity[] gates = Entity.GetEntity(2046).GetField<Entity[]>("terminal_gates");
            StartAsync(terminal_dropTheGates(gates));

            AfterDelay(5000, () => StartAsync(terminal_liftTheGates(gates)));
        }
    }

    private static void syncElementsWithPlayer(Entity player)
    {
        if (Entity.GetEntity(2046).HasField("triggerEnts"))
        {
            List<Entity> triggerEnts = Entity.GetEntity(2046).GetField<List<Entity>>("triggerEnts");
            if (triggerEnts.Count > 0)
            {
                foreach (Entity trigger in triggerEnts)
                    trigger.EnablePlayerUse(player);
            }

            player.OnNotify("trigger_use", monitorTriggerUse);
        }
    }

    private static void monitorTriggerUse(Entity player)
    {
        List<Entity> triggerEnts = Entity.GetEntity(2046).GetField<List<Entity>>("triggerEnts");
        if (triggerEnts.Count == 0) return;
        foreach (Entity triggerEnt in triggerEnts)
        {
            if (player.IsAlive && player.Origin.DistanceTo(triggerEnt.Origin) < 128)
                triggerEnt.GetField<Action<Entity>>("func")(triggerEnt);
            break;
        }
    }

    private static void terminal_toggleLockdown(Entity trigger)
    {
        if (Entity.GetEntity(2046).GetField<string>("state") == "open")
        {
            Entity.GetEntity(2046).SetField("lockdownState", "closing");
            trigger.PlaySound("switch_auto_lights_off");
            trigger.MoveTo(trigger.Origin - new Vector3(0, 0, 5), 1);

            StartAsync(terminal_startLockdown());
        }
        else if (trigger.GetField<string>("state") == "closed")
        {
            Entity.GetEntity(2046).SetField("lockdownState", "opening");
            trigger.PlaySound("switch_auto_lights_on");
            trigger.MoveTo(trigger.Origin + new Vector3(0, 0, 5), .5f);

            StartAsync(terminal_endLockdown(trigger));
        }
    }
    private static IEnumerator terminal_startLockdown()
    {
        Utilities.PrintToConsole("Starting Lockdown");
        if (Entity.GetEntity(2046).GetField<string>("lockdownState") != "open") yield break;
        Utilities.PrintToConsole("Clear for start");

        yield return Wait(1);

        Entity.GetEntity(2046).SetField("lockdownState", "closing");

        Entity[] soundOrigins = getAllEntitiesWithName("alarm_sound_origin");
        Entity[] lightOrigins = getAllEntitiesWithName("alarm_light_origin");

        foreach (Entity sound in soundOrigins)
            OnInterval(2000, () => terminal_playAlarmSound(sound));
        foreach (Entity lights in lightOrigins)
        {
            Entity fx = SpawnFX(mapEffects["fx_redGlow"], lights.Origin);
            lights.SetField("light", fx);
            TriggerFX(fx);
        }

        yield return Wait(1); 

        StartAsync(terminal_dropTheGates(Entity.GetEntity(2046).GetField<Entity[]>("terminal_gates")));
        StartAsync(terminal_closeTheDoors(Entity.GetEntity(2046).GetField<Entity[]>("terminal_doors")));

        yield return Wait(2);
        Entity.GetEntity(2046).SetField("lockdownState", "closed");
    }
    private static IEnumerator terminal_endLockdown(Entity button)
    {
        yield return Wait(1);

        Entity[] lightOrigins = getAllEntitiesWithName("alarm_light_origin");

        foreach (Entity lights in lightOrigins)
        {
            Entity fx = lights.GetField<Entity>("light");
            fx.Delete();
        }

        yield return Wait(1);

        StartAsync(terminal_liftTheGates(button.GetField<Entity[]>("gates")));
        StartAsync(terminal_openTheDoors(button.GetField<Entity[]>("doors")));

        yield return Wait(3);
        Entity.GetEntity(2046).SetField("lockdownState", "open");
    }
    private static bool terminal_playAlarmSound(Entity sound)
    {
        if (Entity.GetEntity(2046).GetField<string>("lockdownState") != "closed" && Entity.GetEntity(2046).GetField<string>("lockdownState") != "closing") return false;
        PlaySoundAtPos(sound.Origin, "alarm_metal_detector");
        return true;
    }

    private static void temp_nullFunc(Entity ent)
    {

    }

    private static void underground_openDoor(Entity triggerEnt)
    {
        if (triggerEnt.GetField<bool>("triggered")) return;

        Entity door = triggerEnt.GetField<Entity>("door");
        Entity hinge = door.GetField<Entity>("hinge");

        hinge.RotateTo(hinge.Angles + new Vector3(0, 90, 0), 1);
        hinge.PlaySound("physics_trashcan_lid_metal");
        triggerEnt.SetField("triggered", true);
        Entity.GetEntity(2046).GetField<List<Entity>>("triggerEnts").Remove(triggerEnt);
        triggerEnt.Delete();
    }
    private static void alpha_openGate(Entity triggerEnt)
    {
        if (triggerEnt.GetField<bool>("triggered")) return;

        Entity door = triggerEnt.GetField<Entity>("door");
        Entity hinge = door.GetField<Entity>("hinge");

        Vector3 openDir = new Vector3(0, 90, 0);
        if (triggerEnt.GetField<string>("side") == "left") openDir = new Vector3(0, -90, 0);
        hinge.RotateTo(hinge.Angles + openDir, 1);
        hinge.PlaySound("physics_trashcan_lid_metal");
        triggerEnt.SetField("triggered", true);
        Entity.GetEntity(2046).GetField<List<Entity>>("triggerEnts").Remove(triggerEnt);
        triggerEnt.Delete();
    }
    private static void paris_toggleTV(Entity tv)
    {
        bool isOn = tv.GetField<bool>("isOn");
        Utilities.PrintToConsole("TV is " + isOn.ToString());

        if (isOn)
        {
            tv.GetField<Entity>("tv").HidePart("tag_fx");
            tv.SetField("isOn", false);
        }
        else
        {
            tv.GetField<Entity>("tv").ShowPart("tag_fx");
            tv.SetField("isOn", true);
        }
    }

    private static void radar_monitorLadderDamage(Entity ladder, int damage, Entity attacker, Vector3 direction_vec, Vector3 point, string mod, string modelName, string partName, string tagName, int dFlags, string weapon)
    {
        if (mod != "MOD_EXPLOSIVE" && mod != "MOD_GRENADE_SPLASH" && mod != "MOD_PROJECTILE_SPLASH") return;
        if (weapon == "flash_grenade_mp" || weapon == "concussion_grenade_mp" || weapon == "emp_grenade_mp") return;

        if (ladder.HasField("parent"))
        {
            radar_monitorLadderDamage(ladder.GetField<Entity>("parent"), damage, attacker, direction_vec, point, mod, modelName, partName, tagName, dFlags, weapon);
            return;
        }

        if (!ladder.HasField("hasBeenBroken"))
        {
            Entity[] col = new Entity[3];
            for (int i = 1; i < col.Length+1; i++)
            {
                col[i - 1] = Spawn("script_model", ladder.Origin + new Vector3(0, 0, 50 * i));
                col[i - 1].SetModel("com_plasticcase_friendly");
                col[i - 1].Hide();
                col[i - 1].Angles = ladder.Angles + new Vector3(90, 0, 0);
                col[i - 1].CloneBrushModelToScriptModel(collision);
                col[i - 1].SetContents(1);
                col[i - 1].SetField("parent", ladder);
                col[i - 1].SetCanDamage(true);
                col[i - 1].SetCanRadiusDamage(true);
                col[i-1].OnNotify("damage", (ent, d, a, dir, p, m, model, part, tag, iDFlags, w) => radar_monitorLadderDamage(ent, d.As<int>(), a.As<Entity>(), dir.As<Vector3>(), p.As<Vector3>(), m.As<string>(), model.As<string>(), part.As<string>(), tag.As<string>(), iDFlags.As<int>(), w.As<string>()));
                if (i == 2) ladder.LinkTo(col[1]);
                else col[i - 1].LinkTo(ladder);
            }

            for (int i = 1; i < col.Length+1; i++)
            {
                Entity col_static = Spawn("script_model", ladder.Origin + new Vector3(0, 0, 60 * i));
                col_static.CloneBrushModelToScriptModel(collision);
                col_static.Angles = ladder.Angles + new Vector3(90, 0, 0);
                col_static.SetContents(1);
            }

            //ladder.SetCanDamage(false);
            //ladder.SetCanRadiusDamage(false);

            Vector3 force = direction_vec;
            force.Normalize();
            //force *= -1;
            force *= 10;

            col[1].PhysicsLaunchServer(Vector3.Zero, force);
            ladder.SetField("hasBeenBroken", true);
            ladder.SetField("col_root", col[1]);
        }
        else if (ladder.HasField("col_root"))
        {
            Entity col = ladder.GetField<Entity>("col_root");
            Vector3 force = direction_vec;
            force.Normalize();
            //force *= -1;
            force *= 10;

            col.PhysicsLaunchServer(Vector3.Zero, force);
        }
    }

    private static void alpha_monitorCivDamage(Entity civ, int damage, Entity attacker, Vector3 direction_vec, Vector3 point, string mod, string modelName, string partName, string tagName, int dFlags, string weapon)
    {
        if (weapon == "flash_grenade_mp" || weapon == "concussion_grenade_mp" || weapon == "emp_grenade_mp") return;

        if (!civ.HasField("hasBeenBroken"))
        {
            civ.SetCanDamage(false);
            civ.SetCanRadiusDamage(false);

            Vector3 force = direction_vec;
            force.Normalize();
            //force *= -1;
            force *= 15;

            civ.GetField<Entity>("parent").ScriptModelClearAnim();
            civ.PhysicsLaunchServer(Vector3.Zero, force);
            civ.SetField("hasBeenBroken", true);
        }
    }

    private void debugEnts()
    {
        int entCount = GetEntArray().GetHashCode();
        Utilities.PrintToConsole("Ent count = " + entCount);
        //Entity[] ents = new Entity[2048];
        List<Entity> ents = new List<Entity>();
        for (int i = 0; i < 2047; i++)
        {
            Entity e = Entity.GetEntity(i);
            ents.Add(e);
        }
        StreamWriter debugLog = new StreamWriter("scripts\\entityDebugLog.txt", true);
        int worldNum = WorldEntNumber();
        debugLog.WriteLine("Entity data for map {0} (worldspawn ent# {1}):", mapname, worldNum);
        foreach (Entity ent in ents)
        {
            //foreach (Entity e in entBank[key])
            {
                string targetname = "";
                string classname = "";
                string target = "";
                int spawnflags = -1;
                string code_classname = "";
                string model = "";
                targetname = ent.TargetName;
                classname = ent.Classname;
                target = ent.Target;
                spawnflags = ent.SpawnFlags;
                code_classname = ent.Code_Classname;
                model = ent.Model;

                debugLog.WriteLine("Entity {0}; targetname = {1}; classname = {3}; target = {4}; spawnflags = {5}; code_classname = {6}; model = {2}", ent.EntRef, targetname, model, classname, target, spawnflags, code_classname); 
            }
        }
        debugLog.Flush();
        debugLog.Close();
        debugLog.Dispose();
    }

    private void initEntityList()
    {
        int worldNum = WorldEntNumber();
        //worldspawn = Call<Entity>("getent", "worldspawn", "targetname");
        //Dictionary<string, List<Entity>> entBank = new Dictionary<string, List<Entity>>();
        //entBank.Add("dynamic_model", new List<Entity>());//Init our dynamic elements list
        for (int i = 18; i < worldNum; i++)
        {
            Entity e = Entity.GetEntity(i);
            string targetname = e.GetField<string>("targetname");
            if (targetname == "" || targetname == null || targetname == "worldspawn") continue;
            //else if (!entBank.ContainsKey(targetname)) entBank.Add(targetname, new List<Entity>());
            //entBank[targetname].Add(e);
        }
    }
    public static Entity[] getAllEntitiesWithName(string targetname)
    {
        int entCount = GetEntArray(targetname, "targetname").GetHashCode();
        Entity[] ret = new Entity[entCount];
        int count = 0;
        for (int i = 0; i < 2000; i++)
        {
            Entity e = Entity.GetEntity(i);
            string t = e.TargetName;
            if (t == targetname) ret[count] = e;
            else continue;
            count++;
            if (count == entCount) break;
        }
        return ret;
    }
    public static Entity[] getAllEntitiesWithClassname(string classname)
    {
        int entCount = GetEntArray(classname, "classname").GetHashCode();
        Entity[] ret = new Entity[entCount];
        int count = 0;
        for (int i = 0; i < 2000; i++)
        {
            Entity e = Entity.GetEntity(i);
            string c = e.Classname;
            if (c == classname) ret[count] = e;
            else continue;
            count++;
            if (count == entCount) break;
        }
        return ret;
    }

    private static void initDynElems()
    {
        switch (mapname)
        {
            case "mp_dome":
                //Improve windsock col
                PreCacheMpAnim("windmill_spin_med");
                PreCacheMpAnim("windmill_spin_med");
                PreCacheMpAnim("foliage_desertbrush_1_sway");
                PreCacheMpAnim("oilpump_pump01");
                PreCacheMpAnim("oilpump_pump02");
                PreCacheMpAnim("windsock_large_wind_medium");
                foreach (Entity e in getAllEntitiesWithName("animated_model"))
                {
                    string model = e.Model;
                    if (model.StartsWith("fence_tarp_"))
                    {
                        e.TargetName = "dynamic_model";
                        PreCacheMpAnim(model + "_med_01");
                        e.ScriptModelPlayAnim(model + "_med_01");
                    }
                    else if (model == "machinery_windmill")
                    {
                        e.TargetName = "dynamic_model";
                        e.ScriptModelPlayAnim("windmill_spin_med");
                    }
                    else if (model.Contains("foliage"))
                    {
                        e.TargetName = "dynamic_model";
                        e.ScriptModelPlayAnim("foliage_desertbrush_1_sway");
                    }
                    else if (model.Contains("oil_pump_jack"))
                    {
                        e.TargetName = "dynamic_model";
                        e.ScriptModelPlayAnim("oilpump_pump0" + (new Random().Next(2)+1));
                    }
                    else if (model == "accessories_windsock_large")
                    {
                        e.TargetName = "dynamic_model";
                        e.ScriptModelPlayAnim("windsock_large_wind_medium");
                    }
                }
                int fire = LoadFX("fire/firelp_huge_pm_nolight_burst");
                int burn = LoadFX("fire/ballistic_vest_death");
                int smoke = LoadFX("smoke/bg_smoke_plume_mp");
                mapEffects.Add("fireFx", fire);
                mapEffects.Add("burnFx", burn);
                mapEffects.Add("smokeFx", smoke);
                Entity.GetEntity(2046).SetField("nukeDetonated", false);
                StartAsync(dome_elems());
                break;
            case "mp_underground":
                Entity[] doors = getAllEntitiesWithName("docks_gate_door");
                List<Entity> triggerEnts_underground = new List<Entity>();

                foreach (Entity door in doors)
                {
                    Entity trigger = Spawn("script_model", door.Origin);
                    trigger.SetCursorHint("HINT_NOICON");
                    trigger.SetHintString("Press ^3[{+activate}]^7 to open the gate");
                    trigger.MakeUsable();
                    trigger.SetField("triggered", false);
                    trigger.SetField("door", door);
                    trigger.SetField("func", new Parameter(new Action<Entity>(underground_openDoor)));
                    triggerEnts_underground.Add(trigger);

                    Vector3 doorAngles_right = AnglesToForward(door.Angles);
                    Entity hinge = Spawn("script_model", door.Origin + (doorAngles_right * 30));
                    hinge.SetModel("tag_origin");
                    hinge.Angles = door.Angles;
                    door.LinkTo(hinge);
                    door.SetField("hinge", hinge);
                }

                Entity.GetEntity(2046).SetField("triggerEnts", new Parameter(triggerEnts_underground));
                break;
            case "mp_alpha":
                Entity door_left = GetEnt("tunnel_door_right", "targetname");
                Entity door_right = GetEnt("tunnel_door_left", "targetname");
                List<Entity> triggerEnts_alpha = new List<Entity>();

                Entity trigger_left = Spawn("script_model", door_left.Origin);
                trigger_left.SetCursorHint("HINT_NOICON");
                trigger_left.SetHintString("Press ^3[{+activate}]^7 to open the gate");
                trigger_left.MakeUsable();
                trigger_left.SetField("triggered", false);
                trigger_left.SetField("door", door_left);
                trigger_left.SetField("side", "left");
                trigger_left.SetField("func", new Parameter(new Action<Entity>(alpha_openGate)));
                triggerEnts_alpha.Add(trigger_left);

                //Vector3 gateAngles_right = AnglesToRight(door_left.Angles);
                Entity hinge_left = Spawn("script_model", new Vector3(539, -698, 73));
                hinge_left.SetModel("tag_origin");
                hinge_left.Angles = door_left.Angles;
                door_left.LinkTo(hinge_left);
                door_left.SetField("hinge", hinge_left);

                Entity trigger_right = Spawn("script_model", door_right.Origin);
                trigger_right.SetCursorHint("HINT_NOICON");
                trigger_right.SetHintString("Press ^3[{+activate}]^7 to open the gate");
                trigger_right.MakeUsable();
                trigger_right.SetField("triggered", false);
                trigger_right.SetField("door", door_right);
                trigger_right.SetField("side", "right");
                trigger_right.SetField("func", new Parameter(new Action<Entity>(alpha_openGate)));
                triggerEnts_alpha.Add(trigger_right);

                //Vector3 right_right = AnglesToRight(door_right.Angles);
                Entity hinge_right = Spawn("script_model", new Vector3(755, -702, 75));
                hinge_right.SetModel("tag_origin");
                hinge_right.Angles = door_right.Angles;
                door_right.LinkTo(hinge_right);
                door_right.SetField("hinge", hinge_right);

                Entity.GetEntity(2046).SetField("triggerEnts", new Parameter(triggerEnts_alpha));

                //turrets
                Entity turret1Origin = GetEnt("pf1702_auto2108", "targetname");
                Entity turret2Origin = GetEnt("pf1703_auto2108", "targetname");

                Entity turret1 = SpawnTurret("misc_turret", turret1Origin.Origin, "sentry_minigun_mp");
                turret1.Angles = turret1Origin.Angles;
                turret1.SetModel("weapon_minigun");
                turret1.MakeTurretOperable();
                turret1.SetRightArc(30);
                turret1.SetLeftArc(80);
                turret1.SetBottomArc(20);
                turret1.MakeUsable();
                turret1.SetDefaultDropPitch(-89.0f);
                turret1.SetTurretModeChangeWait(true);
                Entity turret2 = SpawnTurret("misc_turret", turret2Origin.Origin, "sentry_minigun_mp");
                turret2.Angles = new Vector3(turret2Origin.Angles.X, turret2Origin.Angles.Y + 180, turret2Origin.Angles.Z);
                turret2.SetModel("weapon_minigun");
                turret2.MakeTurretOperable();
                turret2.SetRightArc(50);
                turret2.SetLeftArc(50);
                turret2.SetBottomArc(20);
                turret2.MakeUsable();
                turret2.SetDefaultDropPitch(-89.0f);
                turret2.SetTurretModeChangeWait(true);

                foreach (Entity e in getAllEntitiesWithName("animated_model"))
                {
                    string model = e.Model;
                    if (model == "alpha_hanging_civs")
                    {
                        Vector3 origin = e.Origin;
                        Vector3 angles = e.Angles;
                        e.Delete();
                        PreCacheMpAnim("alpha_hanging_civs_animated");
                        Entity joe = Spawn("script_model", origin);
                        joe.Angles = angles;
                        joe.SetModel(model);
                        joe.HideAllParts();
                        joe.ShowPart("j_civ01_ropetop");
                        joe.ShowPart("j_civ01_ropeneck");
                        joe.ShowPart("civ01_j_mainroot");
                        joe.ScriptModelPlayAnim("alpha_hanging_civs_animated");
                        Entity joe_col = Spawn("script_model", joe.GetTagOrigin("civ01_j_mainroot"));
                        joe_col.SetModel("com_plasticcase_friendly");
                        joe_col.Hide();
                        joe_col.CloneBrushModelToScriptModel(collision);
                        joe_col.Angles = new Vector3(90, 0, 0);
                        joe_col.SetContents(1);
                        joe.LinkTo(joe_col);
                        joe_col.SetCanDamage(true);
                        joe_col.SetCanRadiusDamage(true);
                        joe_col.SetField("parent", joe);
                        joe_col.OnNotify("damage", (ent, damage, attacker, direction_vec, point, meansOfDeath, modelName, partName, tagName, iDFlags, weapon) => alpha_monitorCivDamage(ent, damage.As<int>(), attacker.As<Entity>(), direction_vec.As<Vector3>(), point.As<Vector3>(), meansOfDeath.As<string>(), modelName.As<string>(), partName.As<string>(), tagName.As<string>(), iDFlags.As<int>(), weapon.As<string>()));

                        Entity schmoe = Spawn("script_model", origin);
                        schmoe.Angles = angles;
                        schmoe.SetModel(model);
                        schmoe.HideAllParts();
                        schmoe.ShowPart("j_civ02_ropetop");
                        schmoe.ShowPart("j_civ02_ropeneck");
                        schmoe.ShowPart("civ02_j_mainroot");
                        schmoe.ScriptModelPlayAnim("alpha_hanging_civs_animated");
                        Entity schmoe_col = Spawn("script_model", schmoe.GetTagOrigin("civ02_j_mainroot"));
                        schmoe_col.SetModel("com_plasticcase_friendly");
                        schmoe_col.Hide();
                        schmoe_col.CloneBrushModelToScriptModel(collision);
                        schmoe_col.Angles = new Vector3(90, 0, 0);
                        schmoe_col.SetContents(1);
                        schmoe.LinkTo(schmoe_col);
                        schmoe_col.SetCanDamage(true);
                        schmoe_col.SetCanRadiusDamage(true);
                        schmoe_col.SetField("parent", schmoe);
                        schmoe_col.OnNotify("damage", (ent, damage, attacker, direction_vec, point, meansOfDeath, modelName, partName, tagName, iDFlags, weapon) => alpha_monitorCivDamage(ent, damage.As<int>(), attacker.As<Entity>(), direction_vec.As<Vector3>(), point.As<Vector3>(), meansOfDeath.As<string>(), modelName.As<string>(), partName.As<string>(), tagName.As<string>(), iDFlags.As<int>(), weapon.As<string>()));

                        Entity billyBob = Spawn("script_model", origin);
                        billyBob.Angles = angles;
                        billyBob.SetModel(model);
                        billyBob.HideAllParts();
                        billyBob.ShowPart("j_civ03_ropetop");
                        billyBob.ShowPart("j_civ03_ropeneck");
                        billyBob.ShowPart("civ03_j_mainroot");
                        billyBob.ScriptModelPlayAnim("alpha_hanging_civs_animated");
                        Entity billyBob_col = Spawn("script_model", billyBob.GetTagOrigin("civ03_j_mainroot"));
                        billyBob_col.SetModel("com_plasticcase_friendly");
                        billyBob_col.Hide();
                        billyBob_col.CloneBrushModelToScriptModel(collision);
                        billyBob_col.Angles = new Vector3(90, 0, 0);
                        billyBob_col.SetContents(1);
                        billyBob.LinkTo(billyBob_col);
                        billyBob_col.SetCanDamage(true);
                        billyBob_col.SetCanRadiusDamage(true);
                        billyBob_col.SetField("parent", billyBob);
                        billyBob_col.OnNotify("damage", (ent, damage, attacker, direction_vec, point, meansOfDeath, modelName, partName, tagName, iDFlags, weapon) => alpha_monitorCivDamage(ent, damage.As<int>(), attacker.As<Entity>(), direction_vec.As<Vector3>(), point.As<Vector3>(), meansOfDeath.As<string>(), modelName.As<string>(), partName.As<string>(), tagName.As<string>(), iDFlags.As<int>(), weapon.As<string>()));

                        Entity rope = Spawn("script_model", origin);
                        rope.Angles = angles;
                        rope.SetModel(model);
                        rope.HidePart("civ01_j_mainroot");
                        rope.HidePart("civ02_j_mainroot");
                        rope.HidePart("civ03_j_mainroot");
                        rope.ScriptModelPlayAnim("alpha_hanging_civs_animated");
                    }
                }
                break;
            case "mp_bootleg":
                mapEffects["fx_dust"] = LoadFX("dust/sniper_dust_kickup_child");
                mapEffects["fx_dust2"] = LoadFX("dust/dust_vehicle_tires");
                StartAsync(bootleg_watchForHeavyRain());
                StartAsync(bootleg_spawnRubble());
                break;
            case "mp_bravo":
                //Spawn a tree near southside spawns and make it destroyable to fall up to the bridge area
                Entity tree = Spawn("script_model", new Vector3(-1259, 667, 915));//915
                tree.Angles = new Vector3(10, 0, 0);
                tree.SetModel("foliage_afr_tree_asipalm_01a");
                tree.SetCanDamage(true);
                tree.SetCanRadiusDamage(false);
                tree.Health = 100;
                tree.SetField("maxHealth", 100);
                tree.SetField("damageTaken", 0);
                //tree.MakeVehicleSolidSphere(164, 550);
                //tree.SetContents(1);
                Entity stump = Spawn("script_model", tree.Origin + new Vector3(5, 13, -60));
                stump.SetModel("foliage_tree_destroyed_fallen_log_a");
                stump.Angles = new Vector3(0, 0, 90);
                stump.Hide();
                tree.SetField("stump", stump);

                Entity[] col = new Entity[10];
                for (int i = 0; i < col.Length; i++)
                {
                    col[i] = Spawn("script_model", tree.Origin - new Vector3(5, 0, 30));
                    col[i].Angles = tree.Angles - new Vector3(92, 0, 0);
                    col[i].SetModel("tag_origin");
                    Vector3 forward = AnglesToForward(col[i].Angles);
                    col[i].Origin += forward * (60 * i);
                    col[i].CloneBrushModelToScriptModel(collision);
                    col[i].SetContents(1);

                    if (i == 0) col[i].LinkTo(tree);
                    else col[i].LinkTo(col[i - 1]);
                }
                tree.SetField("col", new Parameter(col));

                tree.OnNotify("damage", (ent, damage, attacker, direction_vec, point, meansOfDeath, modelName, partName, tagName, iDFlags, weapon) => bravo_monitorTreeDamage(ent, damage.As<int>(), attacker.As<Entity>(), direction_vec.As<Vector3>(), point.As<Vector3>(), meansOfDeath.As<string>(), modelName.As<string>(), partName.As<string>(), tagName.As<string>(), iDFlags.As<int>(), weapon.As<string>()));
                break;
            case "mp_carbon":
                //Ladder brushmodel at 180 & 368
                collision = Entity.GetEntity(180);//name is pf766_auto1
                if (collision.Classname != "script_brushmodel") break;

                carbon_buildLadder(new Vector3(-1835, -3685, 3860), new Vector3(0, 232, 0));
                //carbon_buildLadder(new Vector3(-1852, -3707, 3860), new Vector3(0, 52, 180));
                
                break;
            case "mp_radar":
                /*
                Entity[] flag_descriptors = getAllEntitiesWithName("flag_descriptor");
                Entity[] flags = getAllEntitiesWithName("wind_blown_flag");

                for (int i = 0; i < flags.Length; i++)
                    flags[i].Origin = flag_descriptors[i].Origin;
                    */

                Entity[] guard_tower_parts = getAllEntitiesWithName("guard_tower_part");
                foreach (Entity part in guard_tower_parts)
                {
                    part.SetCanDamage(true);
                    part.SetCanRadiusDamage(true);
                    part.OnNotify("damage", (ent, damage, attacker, direction_vec, point, meansOfDeath, modelName, partName, tagName, iDFlags, weapon) => radar_monitorLadderDamage(ent, damage.As<int>(), attacker.As<Entity>(), direction_vec.As<Vector3>(), point.As<Vector3>(), meansOfDeath.As<string>(), modelName.As<string>(), partName.As<string>(), tagName.As<string>(), iDFlags.As<int>(), weapon.As<string>()));
                }
                break;
            case "mp_paris":
                Entity tv = null;
                List<Entity> triggerEnts_paris = new List<Entity>();

                for (int i = 0; i < 2000; i++)
                {
                    Entity e = Entity.GetEntity(i);
                    string model = e.Model;
                    if (model == "com_tv1_cinematic")
                    {
                        tv = e;
                        break;
                    }
                    else continue;
                }
                if (tv == null)
                {
                    Utilities.PrintToConsole("TV was null");
                    return;
                }
                Entity tv_trigger = GetEnt(tv.Target, "targetname");
                if (tv_trigger == null)
                {
                    Utilities.PrintToConsole("Trigger was null");
                    return;
                }
                tv.Origin = new Vector3(646, -1059, 145);
                tv.Angles = new Vector3(tv.Angles.X, -tv.Angles.Y, tv.Angles.Z);
                tv_trigger.Origin = tv.Origin;
                tv_trigger.SetCursorHint("HINT_ACTIVATE");
                tv_trigger.SetHintString("Press ^3[{+activate}]^7 to interact");
                tv_trigger.MakeUsable();
                tv_trigger.SetField("isOn", false);
                tv_trigger.SetField("tv", tv);
                tv_trigger.SetField("func", new Parameter(new Action<Entity>(paris_toggleTV)));
                triggerEnts_paris.Add(tv_trigger);
                tv.HidePart("tag_fx");

                Entity.GetEntity(2046).SetField("triggerEnts", new Parameter(triggerEnts_paris));
                break;
            case "mp_terminal_cls":
                mapEffects.Add("fx_redGlow", LoadFX("misc/laser_glow"));

                Entity[] gates = new Entity[11];
                Entity[] gates_col = new Entity[2];
                Entity[] dyn_doors = new Entity[2];
                List<Entity> triggerEnts_terminal = new List<Entity>();

                for (int i = 18; i < 1000; i++)
                {
                    Entity ent = GetEntByNum(i);
                    if (ent == null) continue;
                    string targetname = ent.TargetName;
                    if (targetname == null || targetname == "") continue;
                    if (targetname == "gate_gate_closing")
                    {
                        if (gates_col[0] == null) gates_col[0] = ent;
                        else
                        {
                            gates_col[1] = ent;
                            break;
                        }
                    }
                }

                StartAsync(terminal_buildGates(gates, dyn_doors, gates_col));

                /*
                Entity trigger_lockdown = Spawn("script_model", new Vector3(351, 5374, 230));
                trigger_lockdown.SetModel("com_emergencylightcase");
                trigger_lockdown.SetCursorHint("HINT_NOICON");
                trigger_lockdown.SetHintString("Press ^3[{+activate}] ^7to Toggle Lockdown");
                trigger_lockdown.MakeUsable();
                trigger_lockdown.SetField("state", "open");
                trigger_lockdown.SetField("gates", new Parameter(gates));
                trigger_lockdown.SetField("doors", new Parameter(dyn_doors));
                trigger_lockdown.SetField("func", new Parameter(new Action<Entity>(terminal_toggleLockdown)));
                triggerEnts_terminal.Add(trigger_lockdown);

                Entity.GetEntity(2046).SetField("triggerEnts", new Parameter(triggerEnts_terminal));
                */
                Entity.GetEntity(2046).SetField("lockdownState", "open");

                Vector3[] soundLocations = new Vector3[] { new Vector3(2927, 5071, 351), new Vector3(1928, 4871, 415), new Vector3(2685, 3457, 392), new Vector3(1968, 3527, 431), new Vector3(2743, 5211, 415), new Vector3(2120, 5683, 415), new Vector3(1680, 6268, 415), new Vector3(1680, 5763, 415), new Vector3(2495, 6327, 299), new Vector3(1511, 6407, 424), new Vector3(1543, 5587, 415), new Vector3(485, 5520, 488), new Vector3(487, 6290, 481), new Vector3(536, 7467, 415), new Vector3(1543, 6931, 415), new Vector3(47, 5519, 415), new Vector3(541, 4853, 376), new Vector3(1871, 4808, 351), new Vector3(1335, 5631, 334) };
                Vector3[] lightLocations = new Vector3[] { new Vector3(1283, 6720, 431), new Vector3(861, 6720, 431), new Vector3(703, 5696, 671), new Vector3(766, 6363, 671), new Vector3(1442, 6333, 671), new Vector3(1407, 5792, 415), new Vector3(-282, 5694, 687), new Vector3(-191, 6049, 687), new Vector3(-134, 5117, 340), new Vector3(254, 4980, 415), new Vector3(940, 5003, 415), new Vector3(1457, 4998, 415), new Vector3(1721, 6159, 415), new Vector3(1913, 5747, 415), new Vector3(2415, 6286, 559), new Vector3(2672, 6000, 559), new Vector3(2221, 5544, 431), new Vector3(2637, 5319, 431), new Vector3(2767, 4848, 351), new Vector3(2101, 4845, 351) };

                foreach (Vector3 sound in soundLocations)
                {
                    Entity s = Spawn("script_origin", sound);
                    s.TargetName = "alarm_sound_origin";
                }

                foreach (Vector3 light in lightLocations)
                {
                    Entity fx = Spawn("script_origin", light);
                    fx.TargetName = "alarm_light_origin";
                }

                StartAsync(terminal_waitForLockdown());

                break;
            case "mp_interchange":

                break;
            default:
                Utilities.PrintToConsole("Unknown/Unsupported map passed in initDynElems()");
                break;
        }
    }

    public static void updateDamageFeedback(Entity player, string type)
    {
        HudElem hitFeedback = NewClientHudElem(player);
        hitFeedback.HorzAlign = HudElem.HorzAlignments.Center;
        hitFeedback.VertAlign = HudElem.VertAlignments.Middle;
        hitFeedback.X = -12;
        hitFeedback.Y = -12;
        hitFeedback.Archived = false;
        hitFeedback.HideWhenDead = false;
        hitFeedback.Sort = 0;
        hitFeedback.Alpha = 0;

        string shader = "damage_feedback";
        if (type == "deployable_vest")
            shader = "damage_feedback_lightarmor";
        else if (type == "juggernaut")
            shader = "damage_feedback_juggernaut";

        hitFeedback.SetShader(shader, 24, 48);
        hitFeedback.Alpha = 1;
        //player.SetField("hud_damageFeedback", hitFeedback);
        player.PlayLocalSound("player_feedback_hit_alert");

        hitFeedback.FadeOverTime(1);
        hitFeedback.Alpha = 0;
        AfterDelay(1000, () => hitFeedback.Destroy());
    }

    private static void carbon_buildLadder(Vector3 origin, Vector3 angles)
    {
        Entity ladder_mid_top = Spawn("script_model", origin);
        ladder_mid_top.Angles = angles;
        ladder_mid_top.SetModel("com_stepladder_large_closed");
        Entity ladder_mid_top_col = Spawn("script_model", ladder_mid_top.Origin);
        ladder_mid_top_col.Angles = ladder_mid_top.Angles - new Vector3(0, 45, 0);
        Vector3 right = AnglesToForward(ladder_mid_top.Angles);
        Vector3 up = AnglesToUp(ladder_mid_top.Angles);
        ladder_mid_top_col.Origin += up * 30;
        ladder_mid_top_col.Origin += right * 15;
        ladder_mid_top_col.CloneBrushModelToScriptModel(collision);
        ladder_mid_top_col.SetContents(1);
    }

    private static void bravo_monitorTreeDamage(Entity tree, int damage, Entity attacker, Vector3 direction_vec, Vector3 point, string mod, string modelName, string partName, string tagName, int dFlags, string weapon)
    {
        if (weapon == "flash_grenade_mp" || weapon == "concussion_grenade_mp" || weapon == "emp_grenade_mp" || weapon == "throwingknife_mp") return;
        if (mod == "MOD_MELEE") return;

        if (!tree.HasField("hasBeenBroken"))
        {
            tree.SetField("damageTaken", tree.GetField<int>("damageTaken") + damage);
            if (attacker.IsPlayer) updateDamageFeedback(attacker, "");

            if (tree.GetField<int>("damageTaken") >= tree.GetField<int>("maxHealth"))
            {
                tree.GetField<Entity>("stump").Show();

                tree.PlaySound("tree_collapse");
                tree.SetCanDamage(false);
                tree.SetCanRadiusDamage(false);

                tree.RotateTo(tree.Angles + new Vector3(43, 0, 0), 4, 3.5f);

                tree.SetField("hasBeenBroken", true);

                AfterDelay(4000, () =>
                {
                    PlaySoundAtPos(new Vector3(-850, 678, 1270), "explo_tree");

                    foreach (Entity col in tree.GetField<Entity[]>("col"))
                    {
                        foreach (Entity player in Players)
                        {
                            if (!player.IsAlive || player.Classname != "player") continue;

                            if (player.IsTouching(col))
                                player.FinishPlayerDamage(tree, null, player.Health, 0, "MOD_CRUSH", "none", Vector3.Zero, Vector3.Zero, "", 0);
                        }
                    }
                });
            }
        }
    }

    private static IEnumerator bootleg_spawnRubble()
    {
        yield return WaitForFrame();

        Entity[] rubble = new Entity[5];
        rubble[0] = Spawn("script_model", new Vector3(-235, -1511, 163));
        rubble[0].SetModel("ch_crate48x64");
        //rubble[0].CloneBrushModelToScriptModel(collision);
        rubble[1] = Spawn("script_model", new Vector3(-235, -1493, 211));
        rubble[1].SetModel("ch_crate24x36");
        //rubble[1].CloneBrushModelToScriptModel(collision);
        rubble[1].Angles = new Vector3(0, 90, 0);
        rubble[2] = Spawn("script_model", new Vector3(-235, -1526, 211));
        rubble[2].SetModel("ch_crate24x36");
        //rubble[2].CloneBrushModelToScriptModel(collision);
        rubble[3] = Spawn("script_model", new Vector3(-183, -1518, 171));
        rubble[3].SetModel("bc_military_tire04_big");
        rubble[4] = Spawn("script_model", new Vector3(-192, -1516, 195));
        rubble[4].SetModel("bc_military_tire05_big");
        rubble[4].Angles = new Vector3(45, 0, 0);

        //Entity col1 = Spawn("script_model", new Vector3(-225, -1513, 0));
        //col1.SetModel("tag_origin");
        //col1.MakeVehicleSolidCapsule(108, 0, 256);

        Entity[] metal = new Entity[2];

        for (int i = 32; i < 1000; i++)
        {
            Entity e = Entity.GetEntity(i);
            if (e.Model == "me_corrugated_metal8x8")
            {
                if (metal[0] == null) metal[0] = e;
                else
                {
                    metal[1] = e;
                    break;
                }
            }
        }

        if (metal[0] != null && metal[1] != null)
        {
            foreach (Entity sheet in metal)
                sheet.SetField("rubble", new Parameter(rubble));

            bootleg_watchForMetalDamage(metal);
        }
    }
    private static void bootleg_watchForMetalDamage(Entity[] metal)
    {
        metal[0].SetCanDamage(true);
        metal[0].SetCanRadiusDamage(true);
        metal[0].OnNotify("damage", (ent, damage, attacker, direction_vec, point, meansOfDeath, modelName, partName, tagName, iDFlags, weapon) => bootleg_monitorMetalDamage(ent, damage.As<int>(), attacker.As<Entity>(), direction_vec.As<Vector3>(), point.As<Vector3>(), meansOfDeath.As<string>(), modelName.As<string>(), partName.As<string>(), tagName.As<string>(), iDFlags.As<int>(), weapon.As<string>()));
        metal[0].SetField("otherSheet", metal[1]);
        metal[1].SetCanDamage(true);
        metal[1].SetCanRadiusDamage(true);
        metal[1].OnNotify("damage", (ent, damage, attacker, direction_vec, point, meansOfDeath, modelName, partName, tagName, iDFlags, weapon) => bootleg_monitorMetalDamage(ent, damage.As<int>(), attacker.As<Entity>(), direction_vec.As<Vector3>(), point.As<Vector3>(), meansOfDeath.As<string>(), modelName.As<string>(), partName.As<string>(), tagName.As<string>(), iDFlags.As<int>(), weapon.As<string>()));
        metal[1].SetField("otherSheet", metal[0]);
    }
    private static void bootleg_monitorMetalDamage(Entity metal, int damage, Entity attacker, Vector3 direction_vec, Vector3 point, string mod, string modelName, string partName, string tagName, int dFlags, string weapon)
    {
        if (weapon == "flash_grenade_mp" || weapon == "concussion_grenade_mp" || weapon == "emp_grenade_mp") return;

        if (!metal.HasField("hasBeenBroken"))
        {
            metal.SetCanDamage(false);
            metal.SetCanRadiusDamage(false);

            metal.PlaySound("physics_car_hood_default");
            metal.RotateTo(metal.Angles + new Vector3(120, -20, 0), .5f);
            metal.MoveTo(metal.Origin + new Vector3(30, -35, -130), .5f, .2f);
            Entity otherSheet = metal.GetField<Entity>("otherSheet");
            otherSheet.PlaySound("physics_car_hood_default");
            otherSheet.RotateTo(otherSheet.Angles + new Vector3(-110, 20, 0), .5f);
            otherSheet.MoveTo(otherSheet.Origin + new Vector3(-60, -38, -130), .5f, .2f);
            otherSheet.SetCanDamage(false);
            otherSheet.SetCanRadiusDamage(false);

            Entity[] rubble = metal.GetField<Entity[]>("rubble");
            rubble[0].MoveTo(rubble[0].Origin - new Vector3(20, 20, 160), .5f, .1f);
            rubble[0].RotateTo(rubble[0].Angles + new Vector3(0, 10, 0), .5f);
            rubble[1].MoveTo(rubble[1].Origin - new Vector3(-77, 10, 195), .5f, .1f);
            rubble[1].RotateTo(rubble[1].Angles + new Vector3(80, 35, 0), .5f);
            rubble[2].MoveTo(rubble[2].Origin - new Vector3(-44, 35, 190), .5f, .1f);
            rubble[2].RotateTo(rubble[2].Angles + new Vector3(0, -25, 90), .5f);
            rubble[3].PhysicsLaunchServer(Vector3.Zero, new Vector3(-100, 0, 0));
            rubble[4].PhysicsLaunchServer(Vector3.Zero, new Vector3(100, 0, 0));
            //rubble[4].MakeVehicleSolidCapsule(40, 0, 12);

            Entity col = Spawn("script_model", new Vector3(-225, -1513, -15));
            col.SetModel("com_plasticcase_dummy");
            col.Angles = Vector3.Zero;
            col.MakeVehicleSolidCapsule(108, 0, 256);
            col.SetContents(1);
            AfterDelay(250, () =>
            {
                foreach (Entity player in Players)
                {
                    if (player.IsTouching(col))
                        player.FinishPlayerDamage(metal, attacker, player.Health, 0, "MOD_CRUSH", "none", Vector3.Zero, Vector3.Zero, "", 0);
                }
            });

            metal.SetField("hasBeenBroken", true);
            otherSheet.SetField("hasBeenBroken", true);

            AfterDelay(500, () =>
            {
                rubble[0].PlaySound("wood_impact");
                rubble[1].PlaySound("wood_impact");
                rubble[2].PlaySound("wood_impact");

                Entity[] fx = new Entity[3];
                fx[0] = SpawnFX(mapEffects["fx_dust"], new Vector3(-189, -1543, 10));
                fx[1] = SpawnFX(mapEffects["fx_dust"], new Vector3(-315, -1539, 10));
                fx[2] = SpawnFX(mapEffects["fx_dust2"], new Vector3(-252, -1529, 10));
                foreach (Entity effect in fx)
                    TriggerFX(effect);

                AfterDelay(1000, () =>
                {
                    foreach (Entity effect in fx)
                        effect.Delete();
                });
            });
        }
    }

    private static IEnumerator bootleg_watchForHeavyRain()
    {

        string gametype = GetDvar("g_gametype");
        float timeLimit = GetDvarInt("scr_" + gametype + "_timelimit");
        if (timeLimit > 30 || timeLimit == 0) yield break;

        Entity.GetEntity(2046).SetField("rainStarted", true);

        yield return Wait(60 * (timeLimit / 20));

        while (!Entity.GetEntity(2046).HasField("stopRainIncrease"))
        {
            bootleg_increaseRainfall();
            timeLimit = GetDvarInt("scr_" + gametype + "_timelimit");
            yield return Wait(60 * (timeLimit / 20));
        }
    }
    private static void bootleg_increaseRainfall()
    {
        //Utilities.PrintToConsole("Adding rain");
        Entity fx = SpawnFX(LoadFX("weather/rain_mp_bootleg"), Vector3.Zero);
        TriggerFX(fx);
    }

    private static IEnumerator terminal_buildGates(Entity[] gates, Entity[] doors, Entity[] gates_col)
    {
        //gates[1] col is used inside the airport(facing out to the trees)
        //gates[0] col is used outside the airport(looking in)

        //back windows should be outside, front inside

        yield return Wait(.05f);

        //Burgertown gates
        Entity gate1 = Spawn("script_model", gates_col[0].Origin);
        gate1.Angles = Vector3.Zero;
        gate1.CloneBrushModelToScriptModel(gates_col[1]);
        Entity gate1_back = Spawn("script_model", gate1.Origin);
        gate1_back.Angles = Vector3.Zero;
        gate1_back.CloneBrushModelToScriptModel(gates_col[0]);
        gate1_back.LinkTo(gate1);
        gates[0] = gate1;
        gates[0].SetField("moveOffset", 145);
        Entity gate2 = Spawn("script_model", new Vector3(2432, 5090, gate1.Origin.Z));
        gate2.Angles = new Vector3(0, -90, 0);
        gate2.CloneBrushModelToScriptModel(gates_col[0]);
        Entity gate2_back = Spawn("script_model", gate2.Origin);
        gate2_back.Angles = new Vector3(0, -90, 0);
        gate2_back.CloneBrushModelToScriptModel(gates_col[1]);
        gate2_back.LinkTo(gate2);
        gates[1] = gate2;
        gates[1].SetField("moveOffset", 145);

        for (int i = 1; i < 7; i++)
        {
            Entity gatePart = Spawn("script_model", gates[0].Origin - new Vector3(0, 0, 24 * i));
            gatePart.Angles = gates[0].Angles;
            gatePart.CloneBrushModelToScriptModel(gates_col[1]);
            gatePart.LinkTo(gate1_back);
            Entity gatePart_back = Spawn("script_model", gates[0].Origin - new Vector3(0, 0, 24 * i));
            gatePart_back.Angles = gates[0].Angles;
            gatePart_back.CloneBrushModelToScriptModel(gates_col[0]);
            gatePart_back.LinkTo(gatePart);

            Entity gatePart2 = Spawn("script_model", gates[1].Origin + new Vector3(0, 0, 24 * i));
            gatePart2.Angles = gates[1].Angles;
            gatePart2.CloneBrushModelToScriptModel(gates_col[1]);
            gatePart2.LinkTo(gates[1]);
            Entity gatePart2_back = Spawn("script_model", gates[1].Origin + new Vector3(0, 0, 24 * i));
            gatePart2_back.Angles = gates[1].Angles;
            gatePart2_back.CloneBrushModelToScriptModel(gates_col[0]);
            gatePart2_back.LinkTo(gatePart2);
        }

        gates[0].Origin += new Vector3(0, 0, 143);
        //gates[1].Origin += new Vector3(0, 0, 143);

        //Front window gates
        Entity[] windowGates = new Entity[3];
        windowGates[0] = Spawn("script_model", new Vector3(1563, 4783, 300));
        windowGates[0].Angles = new Vector3(0, 90, 0);
        windowGates[0].CloneBrushModelToScriptModel(gates_col[1]);
        gates[2] = windowGates[0];
        gates[2].SetField("moveOffset", 80);
        gates[2].SetField("hideOffset", 200);
        windowGates[1] = Spawn("script_model", new Vector3(1067, 4783, 300));
        windowGates[1].Angles = new Vector3(0, 90, 0);
        windowGates[1].CloneBrushModelToScriptModel(gates_col[1]);
        gates[3] = windowGates[1];
        gates[3].SetField("moveOffset", 80);
        gates[3].SetField("hideOffset", 200);
        windowGates[2] = Spawn("script_model", new Vector3(845, 4783, 300));
        windowGates[2].Angles = new Vector3(0, 90, 0);
        windowGates[2].CloneBrushModelToScriptModel(gates_col[1]);
        gates[4] = windowGates[2];
        gates[4].SetField("moveOffset", 80);
        gates[4].SetField("hideOffset", 200);

        foreach (Entity windowGate in windowGates)
        {
            Entity windowGate_back = Spawn("script_model", windowGate.Origin);
            windowGate_back.Angles = windowGate.Angles;
            windowGate_back.CloneBrushModelToScriptModel(gates_col[0]);
            windowGate_back.LinkTo(windowGate);
            Entity[] midWindowGateParts = new Entity[3];

            for (int i = 0; i < midWindowGateParts.Length; i++)
            {
                Entity windowGatePart = Spawn("script_model", windowGate.Origin + new Vector3(0, 0, 24 * (i + 1)));
                windowGatePart.Angles = windowGate.Angles;
                windowGatePart.CloneBrushModelToScriptModel(gates_col[1]);
                windowGatePart.LinkTo(windowGate);
                windowGatePart.Hide();
                Entity windowGatePart_back = Spawn("script_model", windowGatePart.Origin);
                windowGatePart_back.Angles = windowGatePart.Angles;
                windowGatePart_back.CloneBrushModelToScriptModel(gates_col[0]);
                windowGatePart_back.LinkTo(windowGatePart);
                windowGatePart_back.Hide();
                windowGatePart.SetField("back", windowGatePart_back);
                midWindowGateParts[i] = windowGatePart;
            }
            windowGate.SetField("parts", new Parameter(midWindowGateParts));
        }

        //tanker window gates
        Entity tankerWindowGate = Spawn("script_model", new Vector3(1867, 4223, 439));//322
        tankerWindowGate.Angles = new Vector3(0, 180, 0);
        tankerWindowGate.CloneBrushModelToScriptModel(gates_col[1]);
        Entity tankerWindowGate_back = Spawn("script_model", tankerWindowGate.Origin);
        tankerWindowGate_back.Angles = tankerWindowGate.Angles;
        tankerWindowGate_back.CloneBrushModelToScriptModel(gates_col[0]);
        tankerWindowGate_back.LinkTo(tankerWindowGate);
        Entity[] tankerWindowGateParts = new Entity[4];

        for (int i = 0; i < tankerWindowGateParts.Length; i++)
        {
            Entity windowGatePart = Spawn("script_model", tankerWindowGate.Origin + new Vector3(0, 0, 24 * (i + 1)));
            windowGatePart.Angles = tankerWindowGate.Angles;
            windowGatePart.CloneBrushModelToScriptModel(gates_col[1]);
            windowGatePart.LinkTo(tankerWindowGate);
            windowGatePart.Hide();
            Entity windowGatePart_back = Spawn("script_model", windowGatePart.Origin);
            windowGatePart_back.Angles = windowGatePart.Angles;
            windowGatePart_back.CloneBrushModelToScriptModel(gates_col[0]);
            windowGatePart_back.LinkTo(windowGatePart);
            windowGatePart_back.Hide();
            windowGatePart.SetField("back", windowGatePart_back);
            tankerWindowGateParts[i] = windowGatePart;
        }
        tankerWindowGate.SetField("parts", new Parameter(tankerWindowGateParts));

        Entity tankerWindowGate2 = Spawn("script_model", new Vector3(1867, 4535, 439));//322
        tankerWindowGate2.Angles = new Vector3(0, 180, 0);
        tankerWindowGate2.CloneBrushModelToScriptModel(gates_col[1]);
        Entity tankerWindowGate2_back = Spawn("script_model", tankerWindowGate2.Origin);
        tankerWindowGate2_back.Angles = tankerWindowGate2.Angles;
        tankerWindowGate2_back.CloneBrushModelToScriptModel(gates_col[0]);
        tankerWindowGate2_back.LinkTo(tankerWindowGate2);
        Entity[] tankerWindowGate2Parts = new Entity[4];

        for (int i = 0; i < tankerWindowGate2Parts.Length; i++)
        {
            Entity windowGatePart = Spawn("script_model", tankerWindowGate2.Origin + new Vector3(0, 0, 24 * (i + 1)));
            windowGatePart.Angles = tankerWindowGate2.Angles;
            windowGatePart.CloneBrushModelToScriptModel(gates_col[1]);
            windowGatePart.LinkTo(tankerWindowGate2);
            windowGatePart.Hide();
            Entity windowGatePart_back = Spawn("script_model", windowGatePart.Origin);
            windowGatePart_back.Angles = windowGatePart.Angles;
            windowGatePart_back.CloneBrushModelToScriptModel(gates_col[0]);
            windowGatePart_back.LinkTo(windowGatePart);
            windowGatePart_back.Hide();
            windowGatePart.SetField("back", windowGatePart_back);
            tankerWindowGate2Parts[i] = windowGatePart;
        }
        tankerWindowGate2.SetField("parts", new Parameter(tankerWindowGate2Parts));

        gates[5] = tankerWindowGate;
        gates[5].SetField("moveOffset", 117);
        gates[5].SetField("hideOffset", 475);

        gates[6] = tankerWindowGate2;
        gates[6].SetField("moveOffset", 117);
        gates[6].SetField("hideOffset", 475);

        //cargoSideWindows
        Entity cargoWindowGate = Spawn("script_model", new Vector3(2449, 3158, 439));
        cargoWindowGate.Angles = new Vector3(0, -90, 0);
        cargoWindowGate.CloneBrushModelToScriptModel(gates_col[1]);
        Entity cargoWindowGate_back = Spawn("script_model", cargoWindowGate.Origin);
        cargoWindowGate_back.Angles = cargoWindowGate.Angles;
        cargoWindowGate_back.CloneBrushModelToScriptModel(gates_col[0]);
        cargoWindowGate_back.LinkTo(cargoWindowGate);
        Entity[] cargoWindowGateParts = new Entity[14];
        for (int i = 0; i < cargoWindowGateParts.Length; i++)
        {
            Entity windowGatePart = Spawn("script_model", cargoWindowGate.Origin + new Vector3(0, 0, 24 * (i + 1)));
            windowGatePart.Angles = cargoWindowGate.Angles;
            windowGatePart.CloneBrushModelToScriptModel(gates_col[1]);
            windowGatePart.LinkTo(cargoWindowGate);
            windowGatePart.Hide();
            Entity windowGatePart_back = Spawn("script_model", windowGatePart.Origin);
            windowGatePart_back.Angles = windowGatePart.Angles;
            windowGatePart_back.CloneBrushModelToScriptModel(gates_col[0]);
            windowGatePart_back.LinkTo(windowGatePart);
            windowGatePart_back.Hide();
            windowGatePart.SetField("back", windowGatePart_back);
            cargoWindowGateParts[i] = windowGatePart;
        }
        cargoWindowGate.SetField("parts", new Parameter(cargoWindowGateParts));
        gates[7] = cargoWindowGate;
        gates[7].SetField("moveOffset", 355);
        gates[7].SetField("hideOffset", 140);

        //exit door windows + door
        Entity exitWindowGate = Spawn("script_model", new Vector3(1875, 3745, 439));
        exitWindowGate.Angles = new Vector3(0, 180, 0);
        exitWindowGate.CloneBrushModelToScriptModel(gates_col[1]);
        Entity exitWindowGate_back = Spawn("script_model", exitWindowGate.Origin);
        exitWindowGate_back.Angles = exitWindowGate.Angles;
        exitWindowGate_back.CloneBrushModelToScriptModel(gates_col[0]);
        exitWindowGate_back.LinkTo(exitWindowGate);
        Entity[] exitWindowGateParts = new Entity[15];
        for (int i = 0; i < exitWindowGateParts.Length; i++)
        {
            Entity windowGatePart = Spawn("script_model", exitWindowGate.Origin + new Vector3(0, 0, 24 * (i + 1)));
            windowGatePart.Angles = exitWindowGate.Angles;
            windowGatePart.CloneBrushModelToScriptModel(gates_col[1]);
            windowGatePart.LinkTo(exitWindowGate);
            windowGatePart.Hide();
            Entity windowGatePart_back = Spawn("script_model", windowGatePart.Origin);
            windowGatePart_back.Angles = windowGatePart.Angles;
            windowGatePart_back.CloneBrushModelToScriptModel(gates_col[0]);
            windowGatePart_back.LinkTo(windowGatePart);
            windowGatePart_back.Hide();
            windowGatePart.SetField("back", windowGatePart_back);
            exitWindowGateParts[i] = windowGatePart;
        }
        exitWindowGate.SetField("parts", new Parameter(exitWindowGateParts));
        gates[8] = exitWindowGate;
        gates[8].SetField("moveOffset", 375);
        gates[8].SetField("hideOffset", 150);

        //bar windows
        Entity barWindowGate = Spawn("script_model", new Vector3(2038, 3318, 439));
        barWindowGate.Angles = new Vector3(0, -135, 0);
        barWindowGate.CloneBrushModelToScriptModel(gates_col[1]);
        Entity barWindowGate_back = Spawn("script_model", barWindowGate.Origin);
        barWindowGate_back.Angles = barWindowGate.Angles;
        barWindowGate_back.CloneBrushModelToScriptModel(gates_col[0]);
        barWindowGate_back.LinkTo(barWindowGate);
        Entity[] barWindowGateParts = new Entity[9];
        for (int i = 0; i < barWindowGateParts.Length; i++)
        {
            Entity windowGatePart = Spawn("script_model", barWindowGate.Origin + new Vector3(0, 0, 24 * (i + 1)));
            windowGatePart.Angles = barWindowGate.Angles;
            windowGatePart.CloneBrushModelToScriptModel(gates_col[1]);
            windowGatePart.LinkTo(barWindowGate);
            windowGatePart.Hide();
            Entity windowGatePart_back = Spawn("script_model", windowGatePart.Origin);
            windowGatePart_back.Angles = windowGatePart.Angles;
            windowGatePart_back.CloneBrushModelToScriptModel(gates_col[0]);
            windowGatePart_back.LinkTo(windowGatePart);
            windowGatePart_back.Hide();
            windowGatePart.SetField("back", windowGatePart_back);
            barWindowGateParts[i] = windowGatePart;
        }
        barWindowGate.SetField("parts", new Parameter(barWindowGateParts));
        gates[9] = barWindowGate;
        gates[9].SetField("moveOffset", 242);
        gates[9].SetField("hideOffset", 230);

        //makeup shop windows
        Entity shopWindowGate = Spawn("script_model", new Vector3(-75, 4782, 300));
        shopWindowGate.Angles = new Vector3(0, 90, 0);
        shopWindowGate.CloneBrushModelToScriptModel(gates_col[1]);
        Entity shopWindowGate_back = Spawn("script_model", shopWindowGate.Origin);
        shopWindowGate_back.Angles = shopWindowGate.Angles;
        shopWindowGate_back.CloneBrushModelToScriptModel(gates_col[0]);
        shopWindowGate_back.LinkTo(shopWindowGate);
        Entity[] shopWindowGateParts = new Entity[3];
        for (int i = 0; i < shopWindowGateParts.Length; i++)
        {
            Entity windowGatePart = Spawn("script_model", shopWindowGate.Origin + new Vector3(0, 0, 24 * (i + 1)));
            windowGatePart.Angles = shopWindowGate.Angles;
            windowGatePart.CloneBrushModelToScriptModel(gates_col[1]);
            windowGatePart.LinkTo(shopWindowGate);
            windowGatePart.Hide();
            Entity windowGatePart_back = Spawn("script_model", windowGatePart.Origin);
            windowGatePart_back.Angles = windowGatePart.Angles;
            windowGatePart_back.CloneBrushModelToScriptModel(gates_col[0]);
            windowGatePart_back.LinkTo(windowGatePart);
            windowGatePart_back.Hide();
            windowGatePart.SetField("back", windowGatePart_back);
            shopWindowGateParts[i] = windowGatePart;
        }
        shopWindowGate.SetField("parts", new Parameter(shopWindowGateParts));
        gates[10] = shopWindowGate;
        gates[10].SetField("moveOffset", 80);
        gates[10].SetField("hideOffset", 200);

        Entity.GetEntity(2046).SetField("terminal_gates", new Parameter(gates));

        //Move the real gates out of the map to trick the render system
        gates_col[0].Origin = new Vector3(-275, 9419, 1886);
        gates_col[1].Origin = new Vector3(933, -4016, 1216);

        //plane entrance doors
        Entity planeDoor_right = Spawn("script_model", new Vector3(240, 4653, 190));//X = 295 > 240
        planeDoor_right.Angles = Vector3.Zero;
        planeDoor_right.SetModel("storefront_door03");
        Entity planeDoor_left = Spawn("script_model", new Vector3(472, 4655, 190));//X = 415 > 468
        planeDoor_left.Angles = new Vector3(0, 180, 0);
        planeDoor_left.SetModel("storefront_door03");
        for (int i = 0; i < 3; i++)
        {
            Entity planeDoor_right_col = Spawn("script_model", planeDoor_right.Origin + new Vector3(24 * i, 0, 220));
            planeDoor_right_col.Angles = new Vector3(0, 90, 90);
            planeDoor_right_col.CloneBrushModelToScriptModel(gates_col[1]);
            planeDoor_right_col.SetContents(1);
            planeDoor_right_col.LinkTo(planeDoor_right);
            planeDoor_right_col.Hide();
            Entity planeDoor_right_col_back = Spawn("script_model", planeDoor_right.Origin + new Vector3(24 * i, 0, 220));
            planeDoor_right_col_back.Angles = new Vector3(0, 90, 90);
            planeDoor_right_col_back.CloneBrushModelToScriptModel(gates_col[0]);
            planeDoor_right_col_back.SetContents(1);
            planeDoor_right_col_back.LinkTo(planeDoor_right);
            planeDoor_right_col_back.Hide();

            Entity planeDoor_left_col = Spawn("script_model", planeDoor_left.Origin + new Vector3(-24 * i, 0, 220));
            planeDoor_left_col.Angles = new Vector3(0, 90, 90);
            planeDoor_left_col.CloneBrushModelToScriptModel(gates_col[1]);
            planeDoor_left_col.SetContents(1);
            planeDoor_left_col.LinkTo(planeDoor_left);
            planeDoor_left_col.Hide();
            Entity planeDoor_left_col_back = Spawn("script_model", planeDoor_left.Origin + new Vector3(-24 * i, 0, 220));
            planeDoor_left_col_back.Angles = new Vector3(0, 90, 90);
            planeDoor_left_col_back.CloneBrushModelToScriptModel(gates_col[0]);
            planeDoor_left_col_back.SetContents(1);
            planeDoor_left_col_back.LinkTo(planeDoor_left);
            planeDoor_left_col_back.Hide();
        }

        doors[0] = planeDoor_right;
        doors[0].SetField("moveOffset", 55);
        doors[1] = planeDoor_left;
        doors[1].SetField("moveOffset", -57);

        Entity.GetEntity(2046).SetField("terminal_doors", new Parameter(doors));
    }

    private static IEnumerator terminal_waitForLockdown()
    {
        string gametype = GetDvar("g_gametype");
        float timeLimit = GetDvarInt("scr_" + gametype + "_timelimit");
        if (timeLimit > 30 || timeLimit == 0) yield break;

        Utilities.PrintToConsole("Lockdown in " + ((timeLimit / 2) * 60));
        Entity.GetEntity(2046).SetField("lockdownState", "open");

        yield return Wait((timeLimit / 2) * 60);

        StartAsync(terminal_startLockdown());
        terminal_lockSpawns();
    }

    private static void terminal_lockSpawns()
    {
        string spawnIdentifier = GetDvar("g_gametype");
        if (GetDvar("g_gametype") == "war") spawnIdentifier = "tdm";
        List<Entity> cancelledSpawns = new List<Entity>();
        List<Entity> goodSpawns = new List<Entity>();

        Entity[] baseSpawns = getAllEntitiesWithClassname("mp_" + spawnIdentifier + "_spawn");
        Entity[] startSpawns_axis = getAllEntitiesWithClassname("mp_" + spawnIdentifier + "_spawn_axis_start");
        Entity[] startSpawns_allies = getAllEntitiesWithClassname("mp_" + spawnIdentifier + "_spawn_allies_start");

        Utilities.PrintToConsole(baseSpawns.Length + " base spawns");

        foreach (Entity spawn in baseSpawns)
        {
            if (spawn.Origin.Z > 155 || (spawn.Origin.X > 1856 && spawn.Origin.Y > 3137))
                cancelledSpawns.Add(spawn);
            else goodSpawns.Add(spawn);
        }

        foreach (Entity spawn in startSpawns_allies)
        {
            if (spawn.Origin.Z > 155 || (spawn.Origin.X > 1856 && spawn.Origin.Y > 3137))
                cancelledSpawns.Add(spawn);
            else goodSpawns.Add(spawn);
        }
        foreach (Entity spawn in startSpawns_axis)
        {
            if (spawn.Origin.Z > 155 || (spawn.Origin.X > 1856 && spawn.Origin.Y > 3137))
                cancelledSpawns.Add(spawn);
            else goodSpawns.Add(spawn);
        }

        Utilities.PrintToConsole("Locking " + cancelledSpawns.Count + " spawns");

        foreach (Entity badSpawn in cancelledSpawns)
        {
            badSpawn.Origin = goodSpawns[RandomInt(goodSpawns.Count)].Origin;
            badSpawn.Angles = goodSpawns[RandomInt(goodSpawns.Count)].Angles;
        }
    }
    private static IEnumerator terminal_dropTheGates(Entity[] gates)
    {
        foreach (Entity gate in gates)
        {
            if (gate == null) continue;

            gate.MoveTo(gate.Origin - new Vector3(0, 0, gate.GetField<int>("moveOffset")), 2, 1.5f);
            gate.PlayLoopSound("ugv_engine_high");

            //Special gate trickery
            if (gate.HasField("hideOffset") && gate.HasField("parts"))
            {
                Entity[] gateParts = gate.GetField<Entity[]>("parts");
                int hideOffset = gate.GetField<int>("hideOffset");
                for (int i = 0; i < gateParts.Length; i++)
                {
                    int delay = hideOffset * (i+1);
                    int part = i;
                    AfterDelay(delay, () =>
                    {
                        gateParts[part].Show();
                        gateParts[part].GetField<Entity>("back").Show();
                    });
                }
            }
        }

        yield return Wait(2);

        foreach (Entity gate in gates)
        {
            if (gate == null) continue;

            gate.StopLoopSound();
            gate.PlaySound("physics_car_door_default");
            gate.MoveTo(gate.Origin, 120);
        }
    }
    private static IEnumerator terminal_closeTheDoors(Entity[] doors)
    {
        foreach (Entity door in doors)
        {
            if (door == null) continue;

            door.MoveTo(door.Origin + new Vector3(door.GetField<int>("moveOffset"), 0, 0), 2);
            door.PlayLoopSound("ugv_engine_high");
        }

        yield return Wait(2);

        foreach (Entity door in doors)
        {
            if (door == null) continue;

            door.StopLoopSound();
            door.PlaySound("physics_car_door_default");
        }
    }
    private static IEnumerator terminal_liftTheGates(Entity[] gates)
    {
        foreach (Entity gate in gates)
        {
            gate.MoveTo(gate.Origin + new Vector3(0, 0, gate.GetField<int>("moveOffset")), 3, 1f);
            gate.PlayLoopSound("ugv_engine_high");

            //Special gate trickery
            if (gate.HasField("hideOffset") && gate.HasField("parts"))
            {
                Entity[] gateParts = gate.GetField<Entity[]>("parts");
                Array.Reverse(gateParts);
                int hideOffset = gate.GetField<int>("hideOffset");
                for (int i = 0; i < gateParts.Length; i++)
                {
                    int delay = hideOffset * (i + 1);
                    int part = i;
                    AfterDelay((int)(delay * 2.5f), () =>
                    {
                        gateParts[part].Hide();
                        gateParts[part].GetField<Entity>("back").Hide();
                    });
                }
            }
        }

        yield return Wait(3);

        foreach (Entity gate in gates)
            gate.StopLoopSound();
    }
    private static IEnumerator terminal_openTheDoors(Entity[] doors)
    {
        foreach (Entity door in doors)
        {
            door.MoveTo(door.Origin - new Vector3(door.GetField<int>("moveOffset"), 0, 0), 3);
            door.PlayLoopSound("ugv_engine_high");
        }

        yield return Wait(3);

        foreach (Entity door in doors)
            door.StopLoopSound();
    }

    private static void destroyDestructibles()
    {
        foreach (Entity e in getAllEntitiesWithName("destructible_toy"))
            e.Notify("damage", e.Health, "", new Vector3(0, 0, 0), new Vector3(0, 0, 0), "MOD_EXPLOSIVE", "", "", "", 0, "frag_grenade_mp");
        foreach (Entity e in getAllEntitiesWithName("destructible_vehicle"))
            e.Notify("damage", 999999, "", new Vector3(0, 0, 0), new Vector3(0, 0, 0), "MOD_EXPLOSIVE", "", "", "", 0, "frag_grenade_mp");
        foreach (Entity e in getAllEntitiesWithName("explodable_barrel"))
            e.Notify("damage", 999999, "", new Vector3(0, 0, 0), new Vector3(0, 0, 0), "MOD_EXPLOSIVE", "", "", "", 0, "frag_grenade_mp");
        foreach (Entity e in getAllEntitiesWithName("pipe_shootable"))
            e.Notify("damage", e.Health, Entity.GetEntity(2046), Vector3.RandomXY(), new Vector3(0, 0, 0), "MOD_PISTOL_BULLET");

        int glassCount = GetEntArray("glass", "targetname").GetHashCode();
        for (int i = 0; i < glassCount; i++)
            DestroyGlass(i);
        Notify("game_cleanup");
    }

    private static IEnumerator dome_elems()
    {
        while (!Entity.GetEntity(2046).GetField<bool>("nukeDetonated"))
            yield return Wait(.1f);

        //Plan: On nuke detonation(OnNotify nuke_death), the map will break down. Tarps will burn, towers crash, etc.
        if (hasDoneElems) yield break;
        hasDoneElems = true;
        foreach (Entity e in getAllEntitiesWithName("dynamic_model"))
        {
            string model = e.Model;
            if (model.StartsWith("fence_tarp_"))
            {
                //e.Call("hide");
                Vector3 origin = e.Origin;
                Vector3 angles = e.Angles;
                Vector3 forward = AnglesToForward(angles);
                Vector3 up = AnglesToUp(angles);
                Entity fire = SpawnFX(mapEffects["fireFx"], origin, forward, up);
                TriggerFX(fire);
                AfterDelay(3000, () =>
                    {
                        fire.Delete();
                        PlayFX(mapEffects["burnFx"], e.Origin, forward, up);
                        e.ScriptModelClearAnim();
                        e.Hide();
                    });
                //e.Call("hide");
            }
            else if (model == "machinery_windmill")
            {
                e.RotateRoll(80, 2, .5f, .1f);
                AfterDelay(1000, () => e.ScriptModelClearAnim());
            }
            else if (model.Contains("foliage"))
                e.Origin -= new Vector3(0, 0, 50);
            else if (model.Contains("oil_pump_jack"))
            {
                e.ScriptModelClearAnim();
                //Vector3 angles = e.GetField<Vector3>("angles");
                //Vector3 forward = Call<Vector3>(252, angles);
                //Vector3 up = Call<Vector3>(250, angles);
                //Entity smoke = Call<Entity>(308, mapEffects["smokeFx"], e.Origin, forward, up);
                //Call(309, smoke);
            }
            else if (model == "accessories_windsock_large")
            {
                e.ScriptModelClearAnim();
                e.Origin += new Vector3(0, 0, 20);
                Entity bounds = Spawn("script_model", e.Origin + new Vector3(15, -7, 0));
                Entity bound2 = Spawn("script_model", e.Origin + new Vector3(70, -38, 0));
                bounds.SetModel("com_plasticcase_friendly");
                bound2.SetModel("com_plasticcase_friendly");
                bounds.Hide();
                bound2.Hide();
                bounds.CloneBrushModelToScriptModel(collision);
                bound2.CloneBrushModelToScriptModel(collision);
                bounds.SetContents(1);
                bound2.SetContents(1);
                bounds.Angles = e.Angles + new Vector3(0, 90, 0);
                bound2.Angles = bounds.Angles;
                bounds.EnableLinkTo();
                e.LinkTo(bound2);
                bound2.LinkTo(bounds);
                bounds.PhysicsLaunchServer(Vector3.Zero, new Vector3(-400, -250, 10));
            }
        }
        AfterDelay(500, destroyDestructibles);
    }
}
