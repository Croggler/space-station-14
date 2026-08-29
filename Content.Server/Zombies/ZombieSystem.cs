using Content.Shared.NPC.Prototypes;
using Content.Server.Actions;
using Content.Server.Chat;
using Content.Server.Chat.Systems;
using Content.Server.Emoting.Systems;
using Content.Server.Speech.EntitySystems;
using Content.Shared.Anomaly.Components;
using Content.Shared.Armor;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Components;
using Content.Shared.Cloning.Events;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Roles.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Zombies;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Shared._Starlight.Language.Components;
using Content.Server._Starlight.Medical.Body.Systems;
using Content.Shared.Damage;

namespace Content.Server.Zombies
{
    public sealed partial class ZombieSystem : SharedZombieSystem
    {
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private IPrototypeManager _protoManager = default!;
        [Dependency] private IRobustRandom _random = default!;
        [Dependency] private BloodstreamSystem _bloodstream = default!;
        [Dependency] private DamageableSystem _damageable = default!;
        [Dependency] private ChatSystem _chat = default!;
        [Dependency] private ActionsSystem _actions = default!;
        [Dependency] private AutoEmoteSystem _autoEmote = default!;
        [Dependency] private EmoteOnDamageSystem _emoteOnDamage = default!;
        [Dependency] private MobStateSystem _mobState = default!;
        [Dependency] private SharedPopupSystem _popup = default!;
        [Dependency] private SharedRoleSystem _role = default!;

        public readonly ProtoId<NpcFactionPrototype> Faction = "Zombie";

        public const SlotFlags ProtectiveSlots =
            SlotFlags.FEET |
            SlotFlags.HEAD |
            SlotFlags.EYES |
            SlotFlags.GLOVES |
            SlotFlags.MASK |
            SlotFlags.NECK |
            SlotFlags.INNERCLOTHING |
            SlotFlags.OUTERCLOTHING | // Starlight
            SlotFlags.OUTERCLOTHING2; // Starlight
        

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<ZombieComponent, EmoteEvent>(OnEmote, before:
                new[] { typeof(VocalSystem), typeof(BodyEmotesSystem) });

            SubscribeLocalEvent<ZombieComponent, MeleeHitEvent>(OnMeleeHit);
            SubscribeLocalEvent<ZombieComponent, MobStateChangedEvent>(OnMobState);
            SubscribeLocalEvent<ZombieComponent, CloningEvent>(OnZombieCloning);
            SubscribeLocalEvent<ZombieComponent, TryingToSleepEvent>(OnSleepAttempt);
            SubscribeLocalEvent<ZombieComponent, GetCharactedDeadIcEvent>(OnGetCharacterDeadIC);
            SubscribeLocalEvent<ZombieComponent, GetCharacterUnrevivableIcEvent>(OnGetCharacterUnrevivableIC);
            SubscribeLocalEvent<ZombieComponent, MindAddedMessage>(OnMindAdded);
            SubscribeLocalEvent<ZombieComponent, MindRemovedMessage>(OnMindRemoved);

            SubscribeLocalEvent<PendingZombieComponent, MapInitEvent>(OnPendingMapInit);
            SubscribeLocalEvent<PendingZombieComponent, BeforeRemoveAnomalyOnDeathEvent>(OnBeforeRemoveAnomalyOnDeath);

            SubscribeLocalEvent<IncurableZombieComponent, MapInitEvent>(OnPendingMapInit);

            SubscribeLocalEvent<ZombifyOnDeathComponent, MobStateChangedEvent>(OnDamageChanged);
        }

        private void OnBeforeRemoveAnomalyOnDeath(Entity<PendingZombieComponent> ent, ref BeforeRemoveAnomalyOnDeathEvent args)
        {
            // Pending zombies (e.g. infected non-zombies) do not remove their hosted anomaly on death.
            // Current zombies DO remove the anomaly on death.
            args.Cancelled = true;
        }

        private void OnPendingMapInit(EntityUid uid, IncurableZombieComponent component, MapInitEvent args)
        {
            
            // _actions.AddAction(uid, ref component.Action, component.ZombifySelfActionPrototype);
            _faction.AddFaction(uid, Faction);

            if (HasComp<ZombieComponent>(uid) || HasComp<ZombieImmuneComponent>(uid))
                return;

            
            var infection = EnsureComp<BloodStreamInfectionComponent>(uid);
            //currently commented out because if i read it all correctly you cannot turn yourself on command without this. 
            //currently set for "collapse randomly and rise" rather than enter medbay and istantly turn>bite everyone
            //leaving incurablezombiecomponent and pendingzombiecomponent in files because i think they're connected to the init event and have no clue on changing that
            //EnsureComp<IncurableZombieComponent>(uid);
            infection.InfectiousBiteCount = 3;
            infection.IsInitialInfected = true;
            

        }

        private void OnPendingMapInit(EntityUid uid, PendingZombieComponent component, MapInitEvent args)
        {
            if (_mobState.IsDead(uid))
            {
                ZombifyEntity(uid);
                return;
            }

            component.NextTick = _timing.CurTime + TimeSpan.FromSeconds(1f);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);
            var curTime = _timing.CurTime;

            
            // Increment Infectionlevel for all infected entities
            var infectionQuery = EntityQueryEnumerator<BloodStreamInfectionComponent, MobStateComponent, Shared.Damage.Components.DamageableComponent>(); 
            while (infectionQuery.MoveNext(out var uid, out var infection, out var mobState, out var damage))
            {
                if (infection.NextTickTime > curTime)
                    continue;
                infection.NextTickTime = curTime + TimeSpan.FromSeconds(1f);


                if (!HasComp<ZombieComponent>(uid))
                {

                    //Medieval bloodletting basically, drop your bloodlevel by 50%, drop the infection by the same percent. a painful, yet possible way to drop infection level
                    //inside the "not a zombie yet block" because if you have a zombified heart your blood is entirely infected
                    //more related to this in the part of this block that zombifies you
                    //commented out for now because i dont want to figure out the organ system just yet

                    //infection.BloodLevel = _bloodstream.GetBloodLevel(uid);
                    //if (infection.BloodLevel > 0f)
                    //{
                    //    infection.BloodLossRatio = infection.BloodLevel / infection.PreviousBloodLevel;
                    //    infection.InfectionLevel *= infection.BloodLossRatio;
                    //}
                    //infection.PreviousBloodLevel = infection.BloodLevel;

                    var isDead = _mobState.IsDead(uid, mobState);
                    var isCritical = _mobState.IsCritical(uid, mobState);

                    infection.ProcChance = infection.IsInitialInfected ?
                        (isDead ? .6f : (isCritical ? .06f : 0.038f)) :
                        (isDead ? .6f : (isCritical ? 0.6f : 0.3f));

                    for (int i = 0; i < infection.InfectiousBiteCount; i++)
                    {
                        if (_random.Prob(infection.ProcChance))
                        {
                            infection.InfectionLevel += 1f;
                        }
                    }

                    if (infection.InfectionLevel > 100f)
                    { 
                        infection.InfectionLevel = 100f;
                    }

                    if (infection.InfectionLevel >= 60f)
                    {
                        var damageAmount = infection.IsInitialInfected ? 
                        (_mobState.IsCritical(uid, mobState) ? 0.1f : 0.3f) :
                        (_mobState.IsCritical(uid, mobState) ? 0.1f : 0.3f);
                        
                        if (_mobState.IsDead(uid, mobState) == false)
                        { 
                            _damageable.TryChangeDamage(uid, new DamageSpecifier
                            {
                                DamageDict = new()
                                {
                                    { "Poison", damageAmount }
                                }
                            }, 
                            true, false);
                        }

                    }
                    

                    if (infection.InfectionLevel >= 100f)
                    {
                        ZombifyEntity(uid);

                        //sets previous to 1 here, so when the heart is removed the next bloodlevel check will see the 0 current blood, 
                        //then drop infectionlevel to 0, which will then trigger removal of zombification
                        //commented out for now because i dont want to figure out the organ system just yet

                        //infection.PreviousBloodLevel = 1f;
                    }
                }
                if (HasComp<ZombieComponent>(uid))
                {
                    
                }



            }
            



            // Heal the zombified
            var zombQuery = EntityQueryEnumerator<ZombieComponent, Shared.Damage.Components.DamageableComponent, MobStateComponent>();
            while (zombQuery.MoveNext(out var uid, out var comp, out var damage, out var mobState))
            {
                // Process only once per second
                if (comp.NextTick + TimeSpan.FromSeconds(1) > curTime)
                    continue;

                comp.NextTick = curTime;

                if (_mobState.IsDead(uid, mobState))
                    continue;

                var multiplier = _mobState.IsCritical(uid, mobState)
                    ? comp.PassiveHealingCritMultiplier
                    : 1f;

                // Gradual healing for living zombies.
                _damageable.ChangeDamage((uid, damage), comp.PassiveHealing * multiplier, true, false);
            }
        }

        private void OnSleepAttempt(EntityUid uid, ZombieComponent component, ref TryingToSleepEvent args)
        {
            args.Cancelled = true;
        }

        private void OnGetCharacterDeadIC(EntityUid uid, ZombieComponent component, ref GetCharactedDeadIcEvent args)
        {
            args.Dead = true;
        }

        private void OnGetCharacterUnrevivableIC(EntityUid uid, ZombieComponent component, ref GetCharacterUnrevivableIcEvent args)
        {
            args.Unrevivable = true;
        }

        private void OnEmote(EntityUid uid, ZombieComponent component, ref EmoteEvent args)
        {
            // always play zombie emote sounds and ignore others
            if (args.Handled)
                return;

            _protoManager.Resolve(component.EmoteSoundsId, out var sounds);

            args.Handled = _chat.TryPlayEmoteSound(uid, sounds, args.Emote);
        }

        private void OnMobState(EntityUid uid, ZombieComponent component, MobStateChangedEvent args)
        {
            if (args.NewMobState == MobState.Alive)
            {
                // Groaning when damaged
                EnsureComp<EmoteOnDamageComponent>(uid);
                _emoteOnDamage.AddEmote(uid, "Scream");

                // Random groaning
                EnsureComp<AutoEmoteComponent>(uid);
                _autoEmote.AddEmote(uid, "ZombieGroan");
            }
            else
            {
                // Stop groaning when damaged
                _emoteOnDamage.RemoveEmote(uid, "Scream");

                // Stop random groaning
                _autoEmote.RemoveEmote(uid, "ZombieGroan");
            }
        }

        private float GetZombieInfectionChance(EntityUid uid, ZombieComponent zombieComponent)
        {
            var chance = zombieComponent.BaseZombieInfectionChance;

            var armorEv = new CoefficientQueryEvent(ProtectiveSlots);
            RaiseLocalEvent(uid, armorEv);
            foreach (var resistanceEffectiveness in zombieComponent.ResistanceEffectiveness.DamageDict)
            {
                if (armorEv.DamageModifiers.Coefficients.TryGetValue(resistanceEffectiveness.Key, out var coefficient))
                {
                    // Scale the coefficient by the resistance effectiveness, very descriptive I know
                    // For example. With 30% slash resist (0.7 coeff), but only a 60% resistance effectiveness for slash,
                    // you'll end up with 1 - (0.3 * 0.6) = 0.82 coefficient, or a 18% resistance
                    var adjustedCoefficient = 1 - ((1 - coefficient) * resistanceEffectiveness.Value.Float());
                    chance *= adjustedCoefficient;
                }
            }

            var zombificationResistanceEv = new ZombificationResistanceQueryEvent(ProtectiveSlots);
            RaiseLocalEvent(uid, zombificationResistanceEv);
            chance *= zombificationResistanceEv.TotalCoefficient;

            return MathF.Max(chance, zombieComponent.MinZombieInfectionChance);
        }

        private void OnMeleeHit(Entity<ZombieComponent> entity, ref MeleeHitEvent args)
        {
            if (!args.IsHit)
                return;

            var cannotSpread = HasComp<NonSpreaderZombieComponent>(args.User);

            foreach (var uid in args.HitEntities)
            {
                if (args.User == uid)
                    continue;

                if (!TryComp<MobStateComponent>(uid, out var mobState))
                    continue;

                // Starlight Start
                // Zombies cannot attack initial infected
                if (HasComp<InitialInfectedComponent>(uid))
                {
                    args.Handled = true;
                    _popup.PopupEntity(Loc.GetString("zombie-bite-initialinfected-dissuade"), entity, entity);
                    continue;
                }
                // Starlight End

                if (HasComp<ZombieComponent>(uid) || HasComp<IncurableZombieComponent>(uid))
                {
                    // Don't infect, don't deal damage, do not heal from bites, don't pass go!
                    args.Handled = true;
                    _popup.PopupEntity(Loc.GetString("zombie-bite-zombie-dissuade"), entity, entity); // Starlight
                    continue;
                }

                if (_mobState.IsAlive(uid, mobState))
                {
                    _damageable.TryChangeDamage(args.User, entity.Comp.HealingOnBite, true, false);

                    // If we cannot infect the living target, the zed will just heal itself.
                    //(croggler)note for myself since i forgot earlier, this is where it checks the infection chance
                    if (HasComp<ZombieImmuneComponent>(uid) || cannotSpread || !_random.Prob(GetZombieInfectionChance(uid, entity.Comp)))
                        continue;

                    
                    var infection = EnsureComp<BloodStreamInfectionComponent>(uid);
                    infection.InfectiousBiteCount += 1;
                    
                }
                else
                {
                    if (HasComp<ZombieImmuneComponent>(uid) || cannotSpread
                    || !_random.Prob(GetZombieInfectionChance(uid, entity.Comp))) //Starlight fix: Infection-proof suits don't just lose their resistance on death.
                        continue;

                    
                    // If the target is dead and can be infected, infect and increment infection. 
                    //(unless the zombie sits there hitting it like 10 times about it wont rise immediately, if they stand there hitting it it serves the same purpose as not immediately raising so thats fine)
                    //once crit it should be approx 3-5 infectious bites at 80% chance while not crit, 3 bites is 3*60% chance to increment zombification by 1, so between 1-3 infection per second
                    //which is approximately 30-100s if they stop biting immediately. if they bite the dead body twice at the 3, its 5 bites at 60%, plus 20 initial, which, im not calculating just guessing, should be like, 60s max?
                    //this does hurt snowballing a lot, but it can be changed to balance. it's here so that if its a firefight and someone gets crit but not zombified, they can turn once dragged back to safety in compensation for the slower snowball
                    //otherwise 10 bites on crit and they zombify(probably 8-9 bites tbh)
                    //so no snowballing during firefights, but prolonged further turning after
                    var infection = EnsureComp<BloodStreamInfectionComponent>(uid);
                    infection.InfectiousBiteCount += 1;
                    infection.InfectionLevel += 10f;
                    args.Handled = true;
                    
                }
            }
        }

        /// <summary>
        ///     This is the function to call if you want to unzombify an entity.
        /// </summary>
        /// <param name="source">the entity having the ZombieComponent</param>
        /// <param name="target">the entity you want to unzombify (different from source in case of cloning, for example)</param>
        /// <param name="zombiecomp"></param>
        /// <remarks>
        ///     this currently only restore the skin/eye color from before zombified
        ///     TODO: completely rethink how zombies are done to allow reversal.
        /// </remarks>
        public bool UnZombify(EntityUid source, EntityUid target, ZombieComponent? zombiecomp)
        {
            if (!Resolve(source, ref zombiecomp))
                return false;

            foreach (var (layer, info) in zombiecomp.BeforeZombifiedCustomBaseLayers)
            {
                _humanoidAppearance.SetBaseLayerColor(target, layer, info.Color);
                _humanoidAppearance.SetBaseLayerId(target, layer, info.Id);
            }
            if (TryComp<HumanoidAppearanceComponent>(target, out var appcomp))
            {
                appcomp.EyeColor = zombiecomp.BeforeZombifiedEyeColor;
            }
            _humanoidAppearance.SetSkinColor(target, zombiecomp.BeforeZombifiedSkinColor, false);
            _bloodstream.ChangeBloodReagents(target, zombiecomp.BeforeZombifiedBloodReagents);
            _language.RestoreCache((target, EnsureComp<LanguageCacheComponent>(target))); //Starlight UnZombiby fix
            return true;
        }

        private void OnZombieCloning(Entity<ZombieComponent> ent, ref CloningEvent args)
        {
            UnZombify(ent.Owner, args.CloneUid, ent.Comp);
        }

        // Make sure players that enter a zombie (for example via a ghost role or the mind swap spell) count as an antagonist.
        private void OnMindAdded(Entity<ZombieComponent> ent, ref MindAddedMessage args)
        {
            if (!_role.MindHasRole<ZombieRoleComponent>(args.Mind))
                _role.MindAddRole(args.Mind, "MindRoleZombie", mind: args.Mind.Comp);
        }

        // Remove the role when getting cloned, getting gibbed and borged, or leaving the body via any other method.
        private void OnMindRemoved(Entity<ZombieComponent> ent, ref MindRemovedMessage args)
        {
            _role.MindRemoveRole<ZombieRoleComponent>((args.Mind.Owner,  args.Mind.Comp));
        }
    }
}
