﻿using System.Collections;
using System.Collections.Generic;
using SabberStoneCore.Enums;
using SabberStoneCore.Enchants;
using SabberStoneCore.Exceptions;
using SabberStoneCore.Kettle;
using System.Text;
using SabberStoneCore.Model.Zones;
using System;

namespace SabberStoneCore.Model.Entities
{
	/// <summary>
	/// Exposes the properties for each implementing entity.
	/// </summary>
	public interface IEntity : IEnumerable<KeyValuePair<GameTag, int>>, IModel<IEntity>
	{
		/// <summary>Gets the identifier of this entity (EntityID).</summary>
		/// <value>The identifier.</value>
		int Id { get; }

		/// <summary>Gets the ranking order of the moment this entity was played.</summary>
		/// <value>The ranking order.</value>
		int OrderOfPlay { get; }

		/// <summary>Gets or sets the game instance from which this entity is part of.</summary>
		/// <value>The game instance.</value>
		Game Game { get; set; }

		/// <summary>Gets the card from which this entity was derived from.</summary>
		/// <value>The card object.</value>
		Card Card { get; }

		/// <summary>Gets or sets the owner of this entity, the controller who played the entity.</summary>
		/// <value>The controller/owner object.</value>
		Controller Controller { get; set; }

		/// <summary>Gets or sets the zone in which the entity exists.</summary>
		/// <value><see cref="IZone"/>.</value>
		IZone Zone { get; set; }

		/// <summary>Gets or sets the specific <see cref="GameTag"/> for this entity.</summary>
		/// <value><see cref="Int32"/>.</value>
		/// <param name="t">The gametag which represents a property of this entity.</param>
		/// <returns></returns>
		int this[GameTag t] { get; set; }

		/// <summary>Resets all tags (properties) to default values derived from the orginal card object.</summary>
		void Reset();

		/// <summary>Get all buffs/debuffs aplied to this entity.</summary>
		/// <value><see cref="Enchant"/>. Enchants force a temporary effect, for as long as it's active/in play, on this entity.</value>
		List<Enchant> Enchants { get; }

		/// <summary>Gets all triggers hooked onto this entity.</summary>
		/// <value><see cref="Trigger"/>. Triggers execute a certain effect when its requirements are met.</value>
		List<Trigger> Triggers { get; }
	}

	/// <summary>
	/// The base class of all data-holding/action-performing/visible or invisible objects in a SabberStone game.
	/// An entity is defined as a collection of properties, called Tags.
	///
	/// <seealso cref="HeroPower"/>
	/// <seealso cref="Hero"/>
	/// <seealso cref="Minion"/>
	/// <seealso cref="Spell"/>
	/// </summary>
	/// <seealso cref="IEntity" />
	public abstract partial class Entity : IEntity
	{
		/// <summary>
		/// This object holds the original tag values, defined through the constructor
		/// of this instance.
		/// These tags are usefull when values are needed without any buffs/debuffs applied.
		/// </summary>
		internal EntityData _data { get; private set; }

		public int OrderOfPlay { get; protected set; }

		public Controller Controller { get; set; }

		public Game Game { get; set; }

		public IZone Zone { get; set; }

		public Card Card => _data.Card;

		public List<Enchant> Enchants { get; } = new List<Enchant>();

		public List<Trigger> Triggers { get; } = new List<Trigger>();

		/// <summary>Initializes a new instance of the <see cref="Entity"/> class.</summary>
		/// <param name="game">The game.</param>
		/// <param name="card">The card.</param>
		/// <param name="tags">The tags.</param>
		internal Entity(Game game, Card card, Dictionary<GameTag, int> tags)
		{
			Game = game;
			_data = new EntityData(card, tags);
		}

		#region IMODEL_IMPLEMENTATION
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

		public void Stamp(IModel other)
		{
			Entity entity = other as Entity ?? throw new InvalidOperationException("other's type is incorrect");

			OrderOfPlay = entity.OrderOfPlay;
			_data.Stamp(entity._data);

			entity.Enchants.ForEach(p =>
			{
				Enchant clone = p.Clone(Game);
				clone.ParentContainer = Enchants;
				Enchants.Add(clone);
			});

			entity.Triggers.ForEach(p => Triggers.Add(p.Copy(p.SourceId, Game, p.Turn, Triggers, p.Owner)));
			// Call implementer's version of Stamp.
			InternalStamp(entity);
		}

		public string ToHash(params GameTag[] ignore)
		{
			var str = new StringBuilder();
			str.Append(_data.ToHash(ignore));
			str.Append("[O:");
			str.Append(OrderOfPlay.ToString());
			str.Append($"][EN:{Enchants.Count}");
			Enchants.ForEach(p => str.Append(p.ToHash(ignore)));
			str.Append($"][TR:{Triggers.Count}");
			Triggers.ForEach(p => str.Append(p.ToHash(ignore)));
			str.Append("]");
			// Append implementer's hash string.
			str.Append(InternalToHash(ignore));
			return str.ToString();
		}

		IModel IModel.Clone(Game newGame)
		{
			return Clone(newGame);
		}

		public IEntity Clone(Game newGame)
		{
			Entity deepClone = InternalDeepClone(newGame) ?? throw new InvalidOperationException("The implementing class returned null");
			deepClone.OrderOfPlay = OrderOfPlay;
			deepClone._data = _data.Clone(newGame);
			// TODO; Replace lists with our own containers to easily abstract clone behaviour
			
			Enchants.ForEach(p =>
			{
				Enchant clone = p.Clone(Game);
				clone.ParentContainer = deepClone.Enchants;
				deepClone.Enchants.Add(clone);
			});
			
			Triggers.ForEach(p => deepClone.Triggers.Add(p.Copy(p.SourceId, newGame, p.Turn, deepClone.Triggers, p.Owner)));

			return deepClone;
		}

		protected abstract void InternalStamp(IModel entity);

		protected abstract string InternalToHash(params GameTag[] ignore);

		protected abstract Entity InternalDeepClone(Game newGame);

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
		#endregion

		/// <summary>
		/// Gets the native value with out any enchanment or aura applied.
		/// </summary>
		/// <param name="t"></param>
		/// <returns></returns>
		public int GetNativeGameTag(GameTag t)
		{
			return _data[t];
		}

		/// <summary>
		/// Sets the native value without activating any trigger.
		/// </summary>
		/// <param name="tag"></param>
		/// <param name="value"></param>
		public void SetNativeGameTag(GameTag tag, int value)
		{
			_data[tag] = value;

			if (Game.History && (int)tag < 1000)
				Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, tag, value));
		}

		/// <summary>
		/// This is the call for a gametag value. At this point triggers
		/// and enchants come into play.
		/// Gets are looking for active <see cref="Enchant"/>s on that game tag.
		/// Sets are looking for active <see cref="Trigger"/>s on that game tag.
		/// </summary>
		/// <param name="t"></param>
		/// <returns></returns>
		public int this[GameTag t]
		{
			get
			{
				//if (Card.Name.Equals("Angry Chicken"))
				//Game?.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Entity", $"{this} get org. data {t} = {_data[t]}");

				// Start of with the value set when creating this entity.
				int value = _data[t];

				// Calculate the value by testing and applying all (active) enchantments on this entity.
				// The order IS IMPORTANT;
				// 1. Game enchants
				// 2. Zone enchants
				// 3. Entity enchants
				Game?.Enchants.ForEach(p => value = p.Apply(this, t, value));
				Zone?.Enchants.ForEach(p => value = p.Apply(this, t, value));
				Enchants.ForEach(p => value = p.Apply(this, t, value));

				return value;
			}
			set
			{
				int oldValue = _data[t];
				Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Entity", $"{this} set data {t} to {value} oldvalue {oldValue}");
				//if (oldValue == value && t != GameTag.ZONE_POSITION)
				//{
				//    Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Entity", $"{this} set data {t} to {value} obsolet as value is already that value.");
				//    return;
				//}

				SetNativeGameTag(t, value);

				Game.OnEntityChanged(this, t, oldValue, value);


				// don't trigger on explicit turned off heals or predamage changes ....
				if ((t == GameTag.DAMAGE || t == GameTag.PREDAMAGE) && IsIgnoreDamage)
				{
					return;
				}

				// trigger here
				Game?.Triggers.ForEach(p => p.Change(this, t, oldValue, value));
				Zone?.Triggers.ForEach(p => p.Change(this, t, oldValue, value));
				Triggers.ForEach(p => p.Change(this, t, oldValue, value));
			}
		}

		/// <summary>
		/// All gametag values that where changed after creation are wiped.
		/// Any enchants and trigger is removed.
		/// </summary>
		public virtual void Reset()
		{
			_data.Reset();
			Enchants.Clear();
			Triggers.Clear();
		}

		/// <summary>Builds a new subclass of entity that can be added to a SabberStone game instance.</summary>
		/// <param name="controller">The controller of the entity.</param>
		/// <param name="card">The card from which the entity must be derived.</param>
		/// <param name="tags">The tags preset for the entity.</param>
		/// <param name="zone">The zone in which the entity must spawn.</param>
		/// <param name="id">The EntityID to assign to the newly created entity.</param>
		/// <returns></returns>
		/// <exception cref="EntityException"></exception>
		public static IPlayable FromCard(Controller controller, Card card, Dictionary<GameTag, int> tags = null, IZone zone = null, int id = -1)
		{
			tags = tags ?? new Dictionary<GameTag, int>();
			tags[GameTag.ENTITY_ID] = id > 0 ? id : controller.Game.NextId;
			tags[GameTag.CONTROLLER] = controller.PlayerId;
			tags[GameTag.ZONE] = zone != null ? (int)zone.Type : 0;
			//tags[GameTag.CARD_ID] = card.AssetId;

			IPlayable result = null;
			switch (card.Type)
			{
				case CardType.MINION:
					result = new Minion(controller, card, tags);
					break;

				case CardType.SPELL:
					result = new Spell(controller, card, tags);
					break;

				case CardType.WEAPON:
					result = new Weapon(controller, card, tags);
					break;

				case CardType.HERO:
					tags[GameTag.HEALTH] = card[GameTag.HEALTH];
					tags[GameTag.ZONE] = (int)Enums.Zone.PLAY;
					//tags[GameTag.FACTION] = card[GameTag.FACTION];
					tags[GameTag.CARDTYPE] = card[GameTag.CARDTYPE];
					//tags[GameTag.RARITY] = card[GameTag.RARITY];
					//tags[GameTag.HERO_POWER] = card[GameTag.HERO_POWER];
					result = new Hero(controller, card, tags);
					break;

				case CardType.HERO_POWER:
					tags[GameTag.COST] = card[GameTag.COST];
					tags[GameTag.ZONE] = (int)Enums.Zone.PLAY;
					//tags[GameTag.FACTION] = card[GameTag.FACTION];
					tags[GameTag.CARDTYPE] = card[GameTag.CARDTYPE];
					//tags[GameTag.RARITY] = card[GameTag.RARITY];
					//tags[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] = card[GameTag.COST];
					result = new HeroPower(controller, card, tags);
					break;

				default:
					throw new EntityException($"Couldn't create entity, because of an unknown cardType {card.Type}.");
			}

			// add entity to the game dic
			controller.Game.EntityContainer.Add(result.Id, result);

			// add power history full entity
			if (controller.Game.History)
			{
				controller.Game.PowerHistory.Add(PowerHistoryBuilder.FullEntity(result));
			}

			// add entity to the appropriate zone if it was given
			zone?.Add(result);

			if (result.ChooseOne)
			{
				result.ChooseOnePlayables[0] = id < 0 ? FromCard(controller,
					Cards.FromId(result.Card.Id + "a"),
					new Dictionary<GameTag, int>
					{
						[GameTag.CREATOR] = result.Id,
						[GameTag.PARENT_CARD] = result.Id
					},
					controller.SetasideZone) : controller.SetasideZone.GetAll.Find(p => p[GameTag.CREATOR] == result.Id && p.Card.Id == result.Card.Id + "a");

				result.ChooseOnePlayables[1] = id < 0 ? FromCard(controller,
					Cards.FromId(result.Card.Id + "b"),
					new Dictionary<GameTag, int>
					{
						[GameTag.CREATOR] = result.Id,
						[GameTag.PARENT_CARD] = result.Id
					},
					controller.SetasideZone) : controller.SetasideZone.GetAll.Find(p => p[GameTag.CREATOR] == result.Id && p.Card.Id == result.Card.Id + "b");
			}

			return result;
		}

		public override string ToString()
		{
			return $"'{Card.Name}[{Id}]'";
		}

		public IEnumerator<KeyValuePair<GameTag, int>> GetEnumerator()
		{
			return _data.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}

	public partial class Entity
	{

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

		public int Id => _data[GameTag.ENTITY_ID];

		public bool TurnStart
		{
			get { return this[GameTag.TURN_START] == 1; }
			set { this[GameTag.TURN_START] = value ? 1 : 0; }
		}

		public void TurnEnd()
		{
			this[GameTag.TURN_START] = 0;
		}

		public int Overload
		{
			get { return this[GameTag.OVERLOAD]; }
			set { this[GameTag.OVERLOAD] = value; }
		}

		public int Cost
		{
			get { return this[GameTag.COST]; }
			set { this[GameTag.COST] = value; }
		}

		public bool Combo => this[GameTag.COMBO] == 1;

		public bool ChooseOne
		{
			get { return this[GameTag.CHOOSE_ONE] == 1; }
			set { this[GameTag.CHOOSE_ONE] = value ? 1 : 0; }
		}

		public bool ToBeDestroyed
		{
			get { return this[GameTag.TO_BE_DESTROYED] == 1; }
			set { this[GameTag.TO_BE_DESTROYED] = value ? 1 : 0; }
		}

		public int ZonePosition
		{
			get { return this[GameTag.ZONE_POSITION] - 1; }
			set { this[GameTag.ZONE_POSITION] = value + 1; }
		}

		public int NumTurnsInPlay
		{
			get { return this[GameTag.NUM_TURNS_IN_PLAY]; }
			set { this[GameTag.NUM_TURNS_IN_PLAY] = value; }
		}

		public bool IsExhausted
		{
			get { return this[GameTag.EXHAUSTED] == 1; }
			set { this[GameTag.EXHAUSTED] = value ? 1 : 0; }
		}

		public bool IsSummoned
		{
			get { return this[GameTag.SUMMONED] == 1; }
			set { this[GameTag.SUMMONED] = value ? 1 : 0; }
		}

		public bool IsIgnoreDamage
		{
			get { return this[GameTag.IGNORE_DAMAGE] == 1; }
			set { this[GameTag.IGNORE_DAMAGE] = value ? 1 : 0; }
		}

		public int JadeGolem
		{
			get { return this[GameTag.JADE_GOLEM]; }
			set { this[GameTag.JADE_GOLEM] = value; }
		}

		public bool JustPlayed
		{
			get { return this[GameTag.JUST_PLAYED] == 1; }
			set { this[GameTag.JUST_PLAYED] = value ? 1 : 0; }
		}

		public int CardTarget
		{
			get { return this[GameTag.CARD_TARGET]; }
			set { this[GameTag.CARD_TARGET] = value; }
		}

		public bool HasDeathrattle
		{
			get { return this[GameTag.DEATHRATTLE] == 1; }
			set { this[GameTag.DEATHRATTLE] = value ? 1 : 0; }
		}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

	}
}
