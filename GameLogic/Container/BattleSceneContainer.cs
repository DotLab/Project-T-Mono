﻿using GameLogic.CharacterSystem;
using GameLogic.Container.BattleComponent;
using GameLogic.Core;
using GameLogic.Utilities;
using GameLogic.Utilities.ScriptSystem;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GameLogic.Container {
	public class BattleSceneContainer : IJSContextProvider {
		#region Javascript API class
		private sealed class JSAPI : IJSAPI<BattleSceneContainer> {
			private readonly BattleSceneContainer _outer;

			public JSAPI(BattleSceneContainer outer) {
				_outer = outer;
			}

			public BattleSceneContainer Origin(JSContextHelper proof) {
				try {
					if (proof == JSContextHelper.Instance) {
						return _outer;
					}
					return null;
				} catch (Exception) {
					return null;
				}
			}
		}
		#endregion
		private readonly JSAPI _apiObj;

		private static readonly BattleSceneContainer _instance = new BattleSceneContainer();
		public static BattleSceneContainer Instance => _instance;

		private readonly IdentifiedObjList<GridObject> _gridObjList;
		private readonly IdentifiedObjList<LadderObject> _ladderObjList;
		private readonly List<ActableGridObject> _actableObjList;
		private readonly BattleMap _battleMap;
		private ActableGridObject _currentActable = null;
		private int _roundCount = 0;
		private bool _isChecking = false;

		private GridObject _initiative;
		private SkillType _initiativeSkillType;
		private int _initiativeRollPoint;
		private SkillChecker.CharacterAction _checkingAction;
		private List<GridObject> _passives;
		private GridObject _currentPassive;

		public IReadonlyIdentifiedObjList<GridObject> GridObjList => _gridObjList;
		public IReadonlyIdentifiedObjList<LadderObject> LadderObjList => _ladderObjList;
		public IReadOnlyList<ActableGridObject> ActableObjList => _actableObjList;
		public BattleMap BattleMap => _battleMap;
		public ActableGridObject CurrentActable => _currentActable;
		public int RoundCount => _roundCount;
		public bool IsChecking => _isChecking;

		public GridObject Initiative => _initiative;
		public SkillChecker.CharacterAction CheckingAction => _checkingAction;
		public GridObject CurrentPassive => _currentPassive;

		public BattleSceneContainer() {
			_gridObjList = new IdentifiedObjList<GridObject>();
			_ladderObjList = new IdentifiedObjList<LadderObject>();
			_actableObjList = new List<ActableGridObject>();
			_battleMap = new BattleMap();
			_passives = new List<GridObject>();
			_apiObj = new JSAPI(this);
		}

		public void Reset(int rows, int cols) {
			_battleMap.Reset(rows, cols);
			_gridObjList.Clear();
			_ladderObjList.Clear();
			_actableObjList.Clear();
			_currentActable = null;
			_roundCount = 0;
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.Reset(rows, cols);
			}
			Game.DM.Client.BattleScene.Reset(rows, cols);
			this.Update();
		}

		public GridObject FindGridObject(string id) {
			if (_gridObjList.TryGetValue(id, out GridObject gridObject)) {
				return gridObject;
			}
			return null;
		}

		public LadderObject FindLadderObject(string id) {
			if (_ladderObjList.TryGetValue(id, out LadderObject ladderObject)) {
				return ladderObject;
			}
			return null;
		}

		public void PushGridObject(int row, int col, bool highland, GridObject gridObject) {
			if (_gridObjList.Contains(gridObject)) throw new ArgumentException("This object is already added to scene.", nameof(gridObject));
			List<GridObject> land;
			if (highland) land = (List<GridObject>)_battleMap[row, col].Highland;
			else land = (List<GridObject>)_battleMap[row, col].Lowland;
			land.Add(gridObject);
			gridObject.GridRef = _battleMap[row, col];
			gridObject.IsHighland = highland;
			_gridObjList.Add(gridObject);
			if (gridObject is ActableGridObject) {
				_actableObjList.Add((ActableGridObject)gridObject);
				foreach (Player player in Game.Players) {
					player.Client.BattleScene.SetActingOrder(_actableObjList);
				}
				Game.DM.Client.BattleScene.SetActingOrder(_actableObjList);
			}
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.PushGridObject(row, col, highland, gridObject);
			}
			Game.DM.Client.BattleScene.PushGridObject(row, col, highland, gridObject);
		}

		public GridObject PopGridObject(int row, int col, bool highland) {
			GridObject ret = null;
			List<GridObject> land;
			if (highland) land = (List<GridObject>)_battleMap[row, col].Highland;
			else land = (List<GridObject>)_battleMap[row, col].Lowland;
			if (land.Count > 0) {
				ret = land[land.Count - 1];
				land.RemoveAt(land.Count - 1);
				ret.GridRef = null;
				_gridObjList.Remove(ret);
				if (ret is ActableGridObject) {
					_actableObjList.Remove((ActableGridObject)ret);
					foreach (Player player in Game.Players) {
						player.Client.BattleScene.SetActingOrder(_actableObjList);
					}
					Game.DM.Client.BattleScene.SetActingOrder(_actableObjList);
				}
			}
			if (ret != null) {
				foreach (Player player in Game.Players) {
					player.Client.BattleScene.RemoveGridObject(ret);
				}
				Game.DM.Client.BattleScene.RemoveGridObject(ret);
			}
			return ret;
		}

		public bool RemoveGridObject(GridObject gridObject) {
			if (!_gridObjList.Contains(gridObject)) return false;
			if (gridObject.IsHighland) ((List<GridObject>)gridObject.GridRef.Highland).Remove(gridObject);
			else ((List<GridObject>)gridObject.GridRef.Lowland).Remove(gridObject);
			gridObject.GridRef = null;
			_gridObjList.Remove(gridObject);
			if (gridObject is ActableGridObject) {
				_actableObjList.Remove((ActableGridObject)gridObject);
				foreach (Player player in Game.Players) {
					player.Client.BattleScene.SetActingOrder(_actableObjList);
				}
				Game.DM.Client.BattleScene.SetActingOrder(_actableObjList);
			}
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.RemoveGridObject(gridObject);
			}
			Game.DM.Client.BattleScene.RemoveGridObject(gridObject);
			return true;
		}

		public void AddLadderObject(int row, int col, BattleMapDirection direction, LadderObject ladderObject) {
			if (_ladderObjList.Contains(ladderObject)) throw new ArgumentException("This object is already added to scene.", nameof(ladderObject));
			if (row == 0 && direction == BattleMapDirection.NEGATIVE_ROW) throw new ArgumentOutOfRangeException(nameof(direction));
			if (row == _battleMap.Rows - 1 && direction == BattleMapDirection.POSITIVE_ROW) throw new ArgumentOutOfRangeException(nameof(direction));
			if (col == 0 && direction == BattleMapDirection.NEGATIVE_COL) throw new ArgumentOutOfRangeException(nameof(direction));
			if (col == _battleMap.Cols - 1 && direction == BattleMapDirection.POSITIVE_COL) throw new ArgumentOutOfRangeException(nameof(direction));
			Grid grid = _battleMap[row, col];
			if (grid.GetLadder(direction) != null) throw new ArgumentException("This grid has already bound a ladder.", nameof(ladderObject));
			switch (direction) {
				case BattleMapDirection.POSITIVE_ROW: {
						Grid anotherGrid = _battleMap[row + 1, col];
						grid.SetLadderRef(BattleMapDirection.POSITIVE_ROW, ladderObject);
						ladderObject.GridRef = grid;
						anotherGrid.SetLadderRef(BattleMapDirection.NEGATIVE_ROW, ladderObject);
						ladderObject.AnotherGridRef = anotherGrid;
					}
					break;
				case BattleMapDirection.POSITIVE_COL: {
						Grid anotherGrid = _battleMap[row, col + 1];
						grid.SetLadderRef(BattleMapDirection.POSITIVE_COL, ladderObject);
						ladderObject.GridRef = grid;
						anotherGrid.SetLadderRef(BattleMapDirection.NEGATIVE_COL, ladderObject);
						ladderObject.AnotherGridRef = anotherGrid;
					}
					break;
				case BattleMapDirection.NEGATIVE_ROW: {
						Grid anotherGrid = _battleMap[row - 1, col];
						grid.SetLadderRef(BattleMapDirection.NEGATIVE_ROW, ladderObject);
						ladderObject.GridRef = grid;
						anotherGrid.SetLadderRef(BattleMapDirection.POSITIVE_ROW, ladderObject);
						ladderObject.AnotherGridRef = anotherGrid;
					}
					break;
				case BattleMapDirection.NEGATIVE_COL: {
						Grid anotherGrid = _battleMap[row, col - 1];
						grid.SetLadderRef(BattleMapDirection.NEGATIVE_COL, ladderObject);
						ladderObject.GridRef = grid;
						anotherGrid.SetLadderRef(BattleMapDirection.POSITIVE_COL, ladderObject);
						ladderObject.AnotherGridRef = anotherGrid;
					}
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(direction));
			}
			_ladderObjList.Add(ladderObject);
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.AddLadderObject(row, col, direction, ladderObject);
			}
			Game.DM.Client.BattleScene.AddLadderObject(row, col, direction, ladderObject);
		}

		public LadderObject RemoveLadderObject(int row, int col, BattleMapDirection direction) {
			if (row == 0 && direction == BattleMapDirection.NEGATIVE_ROW) return null;
			if (row == _battleMap.Rows - 1 && direction == BattleMapDirection.POSITIVE_ROW) return null;
			if (col == 0 && direction == BattleMapDirection.NEGATIVE_COL) return null;
			if (col == _battleMap.Cols - 1 && direction == BattleMapDirection.POSITIVE_COL) return null;
			Grid grid = _battleMap[row, col];
			if (grid.GetLadder(direction) == null) return null;
			LadderObject ladderObject;
			switch (direction) {
				case BattleMapDirection.POSITIVE_ROW: {
						Grid anotherGrid = _battleMap[row + 1, col];
						ladderObject = grid.PositiveRowLadder;
						ladderObject.GridRef = null;
						ladderObject.AnotherGridRef = null;
						grid.SetLadderRef(BattleMapDirection.POSITIVE_ROW, null);
						anotherGrid.SetLadderRef(BattleMapDirection.NEGATIVE_ROW, null);
					}
					break;
				case BattleMapDirection.POSITIVE_COL: {
						Grid anotherGrid = _battleMap[row, col + 1];
						ladderObject = grid.PositiveColLadder;
						ladderObject.GridRef = null;
						ladderObject.AnotherGridRef = null;
						grid.SetLadderRef(BattleMapDirection.POSITIVE_COL, null);
						anotherGrid.SetLadderRef(BattleMapDirection.NEGATIVE_COL, null);
					}
					break;
				case BattleMapDirection.NEGATIVE_ROW: {
						Grid anotherGrid = _battleMap[row - 1, col];
						ladderObject = grid.NegativeRowLadder;
						ladderObject.GridRef = null;
						ladderObject.AnotherGridRef = null;
						grid.SetLadderRef(BattleMapDirection.NEGATIVE_ROW, null);
						anotherGrid.SetLadderRef(BattleMapDirection.POSITIVE_ROW, null);
					}
					break;
				case BattleMapDirection.NEGATIVE_COL: {
						Grid anotherGrid = _battleMap[row, col - 1];
						ladderObject = grid.NegativeColLadder;
						ladderObject.GridRef = null;
						ladderObject.AnotherGridRef = null;
						grid.SetLadderRef(BattleMapDirection.NEGATIVE_COL, null);
						anotherGrid.SetLadderRef(BattleMapDirection.POSITIVE_COL, null);
					}
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(direction));
			}
			_ladderObjList.Remove(ladderObject);
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.RemoveLadderObject(ladderObject);
			}
			Game.DM.Client.BattleScene.RemoveLadderObject(ladderObject);
			return ladderObject;
		}

		public bool RemoveLadderObject(LadderObject ladderObject) {
			if (ladderObject == null) throw new ArgumentNullException(nameof(ladderObject));
			if (!_ladderObjList.Contains(ladderObject)) return false;
			if (ladderObject.GridRef.PositiveRowLadder == ladderObject) ladderObject.GridRef.SetLadderRef(BattleMapDirection.POSITIVE_ROW, null);
			else if (ladderObject.GridRef.PositiveColLadder == ladderObject) ladderObject.GridRef.SetLadderRef(BattleMapDirection.POSITIVE_COL, null);
			else if (ladderObject.GridRef.NegativeRowLadder == ladderObject) ladderObject.GridRef.SetLadderRef(BattleMapDirection.NEGATIVE_ROW, null);
			else if (ladderObject.GridRef.NegativeColLadder == ladderObject) ladderObject.GridRef.SetLadderRef(BattleMapDirection.POSITIVE_COL, null);
			else return false;
			if (ladderObject.AnotherGridRef.PositiveRowLadder == ladderObject) ladderObject.AnotherGridRef.SetLadderRef(BattleMapDirection.POSITIVE_ROW, null);
			else if (ladderObject.AnotherGridRef.PositiveColLadder == ladderObject) ladderObject.AnotherGridRef.SetLadderRef(BattleMapDirection.POSITIVE_COL, null);
			else if (ladderObject.AnotherGridRef.NegativeRowLadder == ladderObject) ladderObject.AnotherGridRef.SetLadderRef(BattleMapDirection.NEGATIVE_ROW, null);
			else if (ladderObject.AnotherGridRef.NegativeColLadder == ladderObject) ladderObject.AnotherGridRef.SetLadderRef(BattleMapDirection.NEGATIVE_COL, null);
			else return false;
			ladderObject.GridRef = null;
			ladderObject.AnotherGridRef = null;
			_ladderObjList.Remove(ladderObject);
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.RemoveLadderObject(ladderObject);
			}
			Game.DM.Client.BattleScene.RemoveLadderObject(ladderObject);
			return true;
		}

		public void NewRound() {
			foreach (ActableGridObject actableObject in _actableObjList) {
				SkillProperty noticeProperty = actableObject.CharacterRef.GetSkillProperty(SkillType.Notice);
				SkillProperty athleticsProperty = actableObject.CharacterRef.GetSkillProperty(SkillType.Athletics);
				int[] dicePoints = FateDice.Roll();
				if (actableObject.CharacterRef.ControlPlayer != null) {
					actableObject.CharacterRef.ControlPlayer.Client.BattleScene.DisplayDicePoints(actableObject.CharacterRef.ControlPlayer, dicePoints);
				}
				int sumPoint = 0;
				foreach (int point in dicePoints) sumPoint += point;
				actableObject.ActionPriority = noticeProperty.level + sumPoint;
				actableObject.ActionPoint = 1 + athleticsProperty.level >= 1 ? (athleticsProperty.level - 1) / 2 : 0;
				actableObject.MovePoint = 0;
			}
			_actableObjList.Sort((ActableGridObject a, ActableGridObject b) => { return b.ActionPriority - a.ActionPriority; });
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.SetActingOrder(_actableObjList);
			}
			Game.DM.Client.BattleScene.SetActingOrder(_actableObjList);
			if (_actableObjList.Count > 0) {
				_currentActable = _actableObjList[0];
				foreach (Player player in Game.Players) {
					player.Client.BattleScene.ChangeTurn(_currentActable);
				}
				Game.DM.Client.BattleScene.ChangeTurn(_currentActable);
			} else _currentActable = null;
			++_roundCount;
		}

		public void CurrentActionOver() {
			if (_currentActable == null) throw new InvalidOperationException("Current acting character is null.");
			int next = _actableObjList.IndexOf(_currentActable) + 1;
			if (next >= _actableObjList.Count) // new round
			{
				this.NewRound();
			} else {
				_currentActable = _actableObjList[next];
				foreach (Player player in Game.Players) {
					player.Client.BattleScene.ChangeTurn(_currentActable);
				}
				Game.DM.Client.BattleScene.ChangeTurn(_currentActable);
			}
			_currentActable.AddMovePoint();
			this.Update();
		}

		public void Update() {

		}

		public void StartCheck(
			GridObject initiative, IEnumerable<GridObject> passives,
			SkillChecker.CharacterAction action, SkillType initiativeSkillType
			) {
			if (_isChecking) throw new InvalidOperationException("It's in checking state.");
			if (passives == null) throw new ArgumentNullException(nameof(passives));
			if (_passives.Count <= 0) throw new ArgumentException("There is no passive character for checking.", nameof(passives));
			_initiative = initiative ?? throw new ArgumentNullException(nameof(initiative));
			_initiativeSkillType = initiativeSkillType ?? throw new ArgumentNullException(nameof(initiativeSkillType));
			_checkingAction = action;
			_passives.Clear();
			_passives.AddRange(passives);
			_passives.Reverse();
			_currentPassive = null;
			int[] dicePoints = FateDice.Roll();
			_initiativeRollPoint = 0;
			foreach (int point in dicePoints) _initiativeRollPoint += point;
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.DisplayDicePoints(initiative.CharacterRef.Controller, dicePoints);
			}
			Game.DM.Client.BattleScene.DisplayDicePoints(initiative.CharacterRef.Controller, dicePoints);
			_isChecking = true;
			this.NextPassiveCheck();
		}

		private void OnceInitiativeResult(SkillChecker.CheckResult result) {

		}

		private void OncePassiveResult(SkillChecker.CheckResult result) {

		}

		private void OnceCheckOver() {

		}

		public void NextPassiveCheck() {
			if (!_isChecking) throw new InvalidOperationException("It's not in checking state.");
			if (_passives.Count <= 0) {
				_isChecking = false;
				return;
			}
			_currentPassive = _passives[_passives.Count - 1];
			_passives.RemoveAt(_passives.Count - 1);
			SkillChecker.Instance.StartCheck(_initiative.CharacterRef, _currentPassive.CharacterRef, _checkingAction, this.OnceInitiativeResult, this.OncePassiveResult, this.OnceCheckOver);
			SkillChecker.Instance.InitiativeSelectSkill(_initiativeSkillType);
			int[] point = { _initiativeRollPoint };
			SkillChecker.Instance.InitiativeRollDice(point);
			SkillChecker.Instance.InitiativeSkillSelectionOver();
			_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStunt(_checkingAction, _currentPassive, _initiative, _initiativeSkillType);
		}

		private void CurrentPassiveApplySkill(SkillType skillType, bool bigone, int[] fixedDicePoints) {
			SkillChecker.Instance.PassiveSelectSkill(skillType);
			int[] dicePoints = SkillChecker.Instance.PassiveRollDice(fixedDicePoints);
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.DisplayDicePoints(_currentPassive.CharacterRef.Controller, dicePoints);
				player.Client.BattleScene.DisplaySkillReady(_currentPassive, skillType, bigone);
				player.Client.BattleScene.UpdateSumPoint(_currentPassive, SkillChecker.Instance.PassivePoint);
			}
			Game.DM.Client.BattleScene.DisplayDicePoints(_currentPassive.CharacterRef.Controller, dicePoints);
			Game.DM.Client.BattleScene.DisplaySkillReady(_currentPassive, skillType, bigone);
			Game.DM.Client.BattleScene.UpdateSumPoint(_currentPassive, SkillChecker.Instance.PassivePoint);
		}

		public void CurrentPassiveUseSkill(SkillType skillType, bool force, bool bigone, int[] fixedDicePoints = null) {
			if (skillType == null) throw new ArgumentNullException(nameof(skillType));
			if (force) {
				this.CurrentPassiveApplySkill(skillType, bigone, fixedDicePoints);
				_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStuntComplete();
				SkillChecker.Instance.PassiveSkillSelectionOver();
				_initiative.CharacterRef.Controller.Client.BattleScene.NotifyInitiativeSelectAspect(_initiative);
			} else {
				if (SkillChecker.CanResistSkillWithoutDMCheck(SkillChecker.Instance.InitiativeSkillType, skillType, SkillChecker.Instance.Action)) {
					this.CurrentPassiveApplySkill(skillType, bigone, fixedDicePoints);
					_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStuntComplete();
					SkillChecker.Instance.PassiveSkillSelectionOver();
					_initiative.CharacterRef.Controller.Client.BattleScene.NotifyInitiativeSelectAspect(_initiative);
				} else {
					Game.DM.DMClient.DMCheckDialog.RequestCheck(_currentPassive.CharacterRef.Controller,
						SkillChecker.Instance.Passive.Name + "对" + SkillChecker.Instance.Passive.Name + "使用" + skillType.Name + ",可以吗？",
						result => {
							if (result) {
								this.CurrentPassiveApplySkill(skillType, bigone, fixedDicePoints);
								_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStuntComplete();
								SkillChecker.Instance.PassiveSkillSelectionOver();
								_initiative.CharacterRef.Controller.Client.BattleScene.NotifyInitiativeSelectAspect(_initiative);
							} else {
								_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStuntFailure("DM拒绝了你选择的技能");
							}
						});
				}
			}
		}

		public void CurrentPassiveUseStunt(Stunt stunt) {
			if (stunt == null) throw new ArgumentNullException(nameof(stunt));
			if (stunt.Belong != SkillChecker.Instance.Passive) throw new ArgumentException("This stunt is not belong to passive character.", nameof(stunt));
			if (stunt.NeedDMCheck)
				Game.DM.DMClient.DMCheckDialog.RequestCheck(_currentPassive.CharacterRef.Controller,
					SkillChecker.Instance.Passive.Name + "对" + SkillChecker.Instance.Initiative.Name + "使用" + stunt.Name + ",可以吗？",
					result => {
						if (result) {
							_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStuntComplete();
							stunt.InitiativeEffect.DoAction();
						} else {
							_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStuntFailure("DM拒绝了你选择的特技");
						}
					});
			else {
				_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStuntComplete();
				stunt.InitiativeEffect.DoAction();
			}
		}

		public void InitiativeSelectAspect(Aspect aspect, bool reroll) {
			if (aspect == null) throw new ArgumentNullException(nameof(aspect));
			if (!SkillChecker.Instance.CanInitiativeUseAspect(aspect, out string msg)) {
				_initiative.CharacterRef.Controller.Client.BattleScene.NotifyInitiativeSelectAspectFailure(msg);
				return;
			}
			var ownerGridObject = FindGridObject(aspect.Belong.ID);
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.DisplayUsingAspect(_initiative, ownerGridObject, aspect);
			}
			Game.DM.Client.BattleScene.DisplayUsingAspect(_initiative, ownerGridObject, aspect);
			Game.DM.DMClient.DMCheckDialog.RequestCheck(_initiative.CharacterRef.Controller,
				SkillChecker.Instance.Initiative.Name + "想使用" + aspect.Belong.Name + "的" + aspect.Name + "可以吗？",
				result => {
					if (result) {
						SkillChecker.Instance.InitiativeUseAspect(aspect, reroll);
						foreach (Player player in Game.Players) {
							player.Client.BattleScene.UpdateSumPoint(_initiative, SkillChecker.Instance.InitiativePoint);
						}
						Game.DM.Client.BattleScene.UpdateSumPoint(_initiative, SkillChecker.Instance.InitiativePoint);
						_initiative.CharacterRef.Controller.Client.BattleScene.NotifyInitiativeSelectAspectComplete();
					} else {
						_initiative.CharacterRef.Controller.Client.BattleScene.NotifyInitiativeSelectAspectFailure("DM拒绝了你选择的特征");
					}
				});
		}

		public void InitiativeAspectSelectionOver() {
			if (SkillChecker.Instance.State != SkillChecker.CheckerState.INITIATIVE_ASPECT) throw new InvalidOperationException("State incorrect.");
			_initiative.CharacterRef.Controller.Client.BattleScene.NotifyInitiativeSelectAspectOver();
			SkillChecker.Instance.InitiativeAspectSelectionOver();
			_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectAspect(_currentPassive);
		}

		public void CurrentPassiveSelectAspect(Aspect aspect, bool reroll) {
			if (aspect == null) throw new ArgumentNullException(nameof(aspect));
			if (!SkillChecker.Instance.CanPassiveUseAspect(aspect, out string msg)) {
				_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectAspectFailure(msg);
				return;
			}
			var ownerGridObject = FindGridObject(aspect.Belong.ID);
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.DisplayUsingAspect(_currentPassive, ownerGridObject, aspect);
			}
			Game.DM.Client.BattleScene.DisplayUsingAspect(_currentPassive, ownerGridObject, aspect);
			Game.DM.DMClient.DMCheckDialog.RequestCheck(_currentPassive.CharacterRef.Controller,
				SkillChecker.Instance.Passive.Name + "想使用" + aspect.Belong.Name + "的" + aspect.Name + "可以吗？",
				result => {
					if (result) {
						SkillChecker.Instance.PassiveUseAspect(aspect, reroll);
						foreach (Player player in Game.Players) {
							player.Client.BattleScene.UpdateSumPoint(_currentPassive, SkillChecker.Instance.PassivePoint);
						}
						Game.DM.Client.BattleScene.UpdateSumPoint(_currentPassive, SkillChecker.Instance.PassivePoint);
						_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectAspectComplete();
					} else {
						_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectAspectFailure("DM拒绝了你选择的特征");
					}
				});
		}

		public void CurrentPassiveAspectSelectionOver() {
			if (SkillChecker.Instance.State != SkillChecker.CheckerState.PASSIVE_ASPECT) throw new InvalidOperationException("State incorrect.");
			_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectAspectOver();
			SkillChecker.Instance.PassiveAspectSelectionOver();
			SkillChecker.Instance.EndCheck();
		}

		public IJSContext GetContext() {
			return _apiObj;
		}

		public void SetContext(IJSContext context) { }
	}
}

namespace GameLogic.Container.BattleComponent {
	public class GridObject : IIdentifiable {
		#region Javascript API class
		protected class JSAPI : IJSAPI<GridObject> {
			private readonly GridObject _outer;

			public JSAPI(GridObject outer) {
				_outer = outer;
			}

			public GridObject Origin(JSContextHelper proof) {
				try {
					if (proof == JSContextHelper.Instance) {
						return _outer;
					}
					return null;
				} catch (Exception) {
					return null;
				}
			}
		}
		#endregion
		private readonly JSAPI _apiObj;

		protected readonly Character _characterRef;
		protected bool _isObstacle;
		protected Grid _gridRef;
		protected bool _isHighland;
		protected int _stagnate;
		protected readonly bool _isTerrain;
		protected BattleMapDirection _direction;

		public string ID => _characterRef.ID;

		public string Name { get => _characterRef.Name; set { } }
		public string Description { get => _characterRef.Description; set { } }

		public Character CharacterRef => _characterRef;
		public bool IsObstacle { get => _isObstacle; set => _isObstacle = value; }
		public Grid GridRef { get => _gridRef; set => _gridRef = value; }
		public bool IsHighland { get => _isHighland; set => _isHighland = value; }
		public int Stagnate { get => _stagnate; set => _stagnate = value; }
		public bool IsTerrain => _isTerrain;
		public BattleMapDirection Direction { get => _direction; set => _direction = value; }

		public GridObject(Character characterRef, bool isTerrian) {
			_characterRef = characterRef ?? throw new ArgumentNullException(nameof(characterRef));
			_isTerrain = isTerrian;
			_apiObj = new JSAPI(this);
		}

		public virtual IJSContext GetContext() {
			return _apiObj;
		}

		public void SetContext(IJSContext context) { }
	}

	public sealed class LadderObject : IIdentifiable {
		#region Javascript API class
		private sealed class JSAPI : IJSAPI<LadderObject> {
			private readonly LadderObject _outer;

			public JSAPI(LadderObject outer) {
				_outer = outer;
			}

			public LadderObject Origin(JSContextHelper proof) {
				try {
					if (proof == JSContextHelper.Instance) {
						return _outer;
					}
					return null;
				} catch (Exception) {
					return null;
				}
			}
		}
		#endregion
		private readonly JSAPI _apiObj;

		private readonly Character _characterRef;
		private Grid _gridRef;
		private Grid _anotherGridRef;
		private int _stagnate;
		private BattleMapDirection _direction;

		public string ID => _characterRef.ID;

		public string Name { get => _characterRef.Name; set { } }
		public string Description { get => _characterRef.Description; set { } }

		public Character CharacterRef => _characterRef;
		public Grid GridRef { get => _gridRef; set => _gridRef = value; }
		public Grid AnotherGridRef { get => _anotherGridRef; set => _anotherGridRef = value; }
		public int Stagnate { get => _stagnate; set => _stagnate = value; }
		public BattleMapDirection Direction { get => _direction; set => _direction = value; }

		public LadderObject(Character characterRef) {
			_characterRef = characterRef ?? throw new ArgumentNullException(nameof(characterRef));
			_apiObj = new JSAPI(this);
		}

		public IJSContext GetContext() {
			return _apiObj;
		}

		public void SetContext(IJSContext context) { }
	}

	public sealed class ActableGridObject : GridObject {
		#region Javascript API class
		private new class JSAPI : GridObject.JSAPI, IJSAPI<ActableGridObject> {
			private readonly ActableGridObject _outer;

			public JSAPI(ActableGridObject outer) : base(outer) {
				_outer = outer;
			}

			ActableGridObject IJSAPI<ActableGridObject>.Origin(JSContextHelper proof) {
				try {
					if (proof == JSContextHelper.Instance) {
						return _outer;
					}
					return null;
				} catch (Exception) {
					return null;
				}
			}
		}
		#endregion
		private readonly JSAPI _apiObj;

		private static void GetAroundReachablePlacesRecursively(List<ReachablePlace> list, ReachablePlace center) {
			int leftMovePoint = center.leftMovePoint;
			if (CanMove(center.row, center.col, center.highland, BattleMapDirection.POSITIVE_ROW, false, ref leftMovePoint)) {
				var next = new ReachablePlace();
				next.row = center.row + 1;
				next.col = center.col;
				next.highland = center.highland;
				next.prevPlace = center;
				next.leftMovePoint = leftMovePoint;
				var markedPlace = MarkedReachablePlace(list, next.row, next.col, next.highland);
				if (markedPlace != null) {
					if (markedPlace.leftMovePoint < next.leftMovePoint) {
						list.Remove(markedPlace);
						list.Add(next);
						GetAroundReachablePlacesRecursively(list, next);
					}
				} else {
					list.Add(next);
					GetAroundReachablePlacesRecursively(list, next);
				}
			}
			leftMovePoint = center.leftMovePoint;
			if (CanMove(center.row, center.col, center.highland, BattleMapDirection.POSITIVE_COL, false, ref leftMovePoint)) {
				ReachablePlace next = new ReachablePlace();
				next.row = center.row;
				next.col = center.col + 1;
				next.highland = center.highland;
				next.prevPlace = center;
				next.leftMovePoint = leftMovePoint;
				var markedPlace = MarkedReachablePlace(list, next.row, next.col, next.highland);
				if (markedPlace != null) {
					if (markedPlace.leftMovePoint < next.leftMovePoint) {
						list.Remove(markedPlace);
						list.Add(next);
						GetAroundReachablePlacesRecursively(list, next);
					}
				} else {
					list.Add(next);
					GetAroundReachablePlacesRecursively(list, next);
				}
			}
			leftMovePoint = center.leftMovePoint;
			if (CanMove(center.row, center.col, center.highland, BattleMapDirection.NEGATIVE_ROW, false, ref leftMovePoint)) {
				ReachablePlace next = new ReachablePlace();
				next.row = center.row - 1;
				next.col = center.col;
				next.highland = center.highland;
				next.prevPlace = center;
				next.leftMovePoint = leftMovePoint;
				var markedPlace = MarkedReachablePlace(list, next.row, next.col, next.highland);
				if (markedPlace != null) {
					if (markedPlace.leftMovePoint < next.leftMovePoint) {
						list.Remove(markedPlace);
						list.Add(next);
						GetAroundReachablePlacesRecursively(list, next);
					}
				} else {
					list.Add(next);
					GetAroundReachablePlacesRecursively(list, next);
				}
			}
			leftMovePoint = center.leftMovePoint;
			if (CanMove(center.row, center.col, center.highland, BattleMapDirection.NEGATIVE_COL, false, ref leftMovePoint)) {
				ReachablePlace next = new ReachablePlace();
				next.row = center.row;
				next.col = center.col - 1;
				next.highland = center.highland;
				next.prevPlace = center;
				next.leftMovePoint = leftMovePoint;
				var markedPlace = MarkedReachablePlace(list, next.row, next.col, next.highland);
				if (markedPlace != null) {
					if (markedPlace.leftMovePoint < next.leftMovePoint) {
						list.Remove(markedPlace);
						list.Add(next);
						GetAroundReachablePlacesRecursively(list, next);
					}
				} else {
					list.Add(next);
					GetAroundReachablePlacesRecursively(list, next);
				}
			}
			leftMovePoint = center.leftMovePoint;
			if (CanMove(center.row, center.col, center.highland, BattleMapDirection.POSITIVE_ROW, true, ref leftMovePoint)) {
				ReachablePlace next = new ReachablePlace();
				next.row = center.row + 1;
				next.col = center.col;
				next.highland = !center.highland;
				next.prevPlace = center;
				next.leftMovePoint = leftMovePoint;
				var markedPlace = MarkedReachablePlace(list, next.row, next.col, next.highland);
				if (markedPlace != null) {
					if (markedPlace.leftMovePoint < next.leftMovePoint) {
						list.Remove(markedPlace);
						list.Add(next);
						GetAroundReachablePlacesRecursively(list, next);
					}
				} else {
					list.Add(next);
					GetAroundReachablePlacesRecursively(list, next);
				}
			}
			leftMovePoint = center.leftMovePoint;
			if (CanMove(center.row, center.col, center.highland, BattleMapDirection.POSITIVE_COL, true, ref leftMovePoint)) {
				ReachablePlace next = new ReachablePlace();
				next.row = center.row;
				next.col = center.col + 1;
				next.highland = !center.highland;
				next.prevPlace = center;
				next.leftMovePoint = leftMovePoint;
				var markedPlace = MarkedReachablePlace(list, next.row, next.col, next.highland);
				if (markedPlace != null) {
					if (markedPlace.leftMovePoint < next.leftMovePoint) {
						list.Remove(markedPlace);
						list.Add(next);
						GetAroundReachablePlacesRecursively(list, next);
					}
				} else {
					list.Add(next);
					GetAroundReachablePlacesRecursively(list, next);
				}
			}
			leftMovePoint = center.leftMovePoint;
			if (CanMove(center.row, center.col, center.highland, BattleMapDirection.NEGATIVE_ROW, true, ref leftMovePoint)) {
				ReachablePlace next = new ReachablePlace();
				next.row = center.row - 1;
				next.col = center.col;
				next.highland = !center.highland;
				next.prevPlace = center;
				next.leftMovePoint = leftMovePoint;
				var markedPlace = MarkedReachablePlace(list, next.row, next.col, next.highland);
				if (markedPlace != null) {
					if (markedPlace.leftMovePoint < next.leftMovePoint) {
						list.Remove(markedPlace);
						list.Add(next);
						GetAroundReachablePlacesRecursively(list, next);
					}
				} else {
					list.Add(next);
					GetAroundReachablePlacesRecursively(list, next);
				}
			}
			leftMovePoint = center.leftMovePoint;
			if (CanMove(center.row, center.col, center.highland, BattleMapDirection.NEGATIVE_COL, true, ref leftMovePoint)) {
				ReachablePlace next = new ReachablePlace();
				next.row = center.row;
				next.col = center.col - 1;
				next.highland = !center.highland;
				next.prevPlace = center;
				next.leftMovePoint = leftMovePoint;
				var markedPlace = MarkedReachablePlace(list, next.row, next.col, next.highland);
				if (markedPlace != null) {
					if (markedPlace.leftMovePoint < next.leftMovePoint) {
						list.Remove(markedPlace);
						list.Add(next);
						GetAroundReachablePlacesRecursively(list, next);
					}
				} else {
					list.Add(next);
					GetAroundReachablePlacesRecursively(list, next);
				}
			}
		}

		private static bool CanMove(int srcRow, int srcCol, bool srcHighland, BattleMapDirection direction, bool stairway, ref int leftMovePoint) {
			int dstRow = srcRow;
			int dstCol = srcCol;
			switch (direction) {
				case BattleMapDirection.POSITIVE_ROW:
					++dstRow;
					break;
				case BattleMapDirection.POSITIVE_COL:
					++dstCol;
					break;
				case BattleMapDirection.NEGATIVE_ROW:
					--dstRow;
					break;
				case BattleMapDirection.NEGATIVE_COL:
					--dstCol;
					break;
				default:
					return false;
			}
			if (dstRow < 0 || dstRow >= BattleSceneContainer.Instance.BattleMap.Rows || dstCol < 0 || dstCol >= BattleSceneContainer.Instance.BattleMap.Cols) return false;
			Grid dstGrid = BattleSceneContainer.Instance.BattleMap[dstRow, dstCol];
			bool dstHighland = stairway ^ srcHighland;
			IReadOnlyList<GridObject> land = dstHighland ? dstGrid.Highland : dstGrid.Lowland;
			if (land.Count <= 0 || land[land.Count - 1].IsObstacle) return false;
			if (stairway) {
				LadderObject ladderObject = BattleSceneContainer.Instance.BattleMap[srcRow, srcCol].GetLadder(direction);
				leftMovePoint -= ladderObject.Stagnate;
			}
			GridObject terrian = null;
			for (int i = land.Count - 1; i >= 0; --i) {
				if (land[i].IsTerrain) {
					terrian = land[i];
					break;
				}
			}
			if (terrian == null) return false;
			leftMovePoint -= terrian.Stagnate;
			if (leftMovePoint < 0) return false;
			return true;
		}

		private static ReachablePlace MarkedReachablePlace(List<ReachablePlace> list, int row, int col, bool highland) {
			foreach (var place in list) {
				if (place.row == row && place.col == col && place.highland == highland) return place;
			}
			return null;
		}

		private int _actionPriority = 0;
		private int _actionPoint = 0;
		private bool _movable = false;
		private int _movePoint = 0;

		private List<ReachablePlace> _movepathCache = null;

		public int ActionPriority { get => _actionPriority; set => _actionPriority = value; }
		public int ActionPoint { get => _actionPoint; set => _actionPoint = value; }
		public bool Movable { get => _movable; set => _movable = value; }
		public int MovePoint { get => _movePoint; set => _movePoint = value; }

		public ActableGridObject(Character characterRef) :
			base(characterRef, false) {
			_apiObj = new JSAPI(this);
		}

		public void AddMovePoint() {
			int[] dicePoints = FateDice.Roll();
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.DisplayDicePoints(this.CharacterRef.Controller, dicePoints);
			}
			Game.DM.Client.BattleScene.DisplayDicePoints(this.CharacterRef.Controller, dicePoints);
			int sumPoint = 0;
			foreach (int point in dicePoints) sumPoint += point;
			SkillProperty athleticsProperty = this.CharacterRef.GetSkillProperty(SkillType.Athletics);
			this.MovePoint += athleticsProperty.level + sumPoint;
		}

		public void TakeExtraMove() {
			int leftActionPoint = this.ActionPoint;
			if (--leftActionPoint < 0) throw new InvalidOperationException("Action point is not enough.");
			this.ActionPoint = leftActionPoint;
			this.AddMovePoint();
		}

		public List<ReachablePlace> GetReachablePlaceList() {
			if (_movepathCache != null) return _movepathCache;
			List<ReachablePlace> ret = new List<ReachablePlace>();
			ReachablePlace begin = new ReachablePlace();
			begin.prevPlace = null;
			begin.row = this.GridRef.PosRow;
			begin.col = this.GridRef.PosCol;
			begin.highland = this.IsHighland;
			begin.leftMovePoint = this.MovePoint;
			GetAroundReachablePlacesRecursively(ret, begin);
			_movepathCache = ret;
			return ret;
		}

		public void Move(BattleMapDirection direction, bool stairway) {
			int srcRow = this.GridRef.PosRow;
			int srcCol = this.GridRef.PosCol;
			int dstRow = srcRow;
			int dstCol = srcCol;
			switch (direction) {
				case BattleMapDirection.POSITIVE_ROW:
					++dstRow;
					break;
				case BattleMapDirection.POSITIVE_COL:
					++dstCol;
					break;
				case BattleMapDirection.NEGATIVE_ROW:
					--dstRow;
					break;
				case BattleMapDirection.NEGATIVE_COL:
					--dstCol;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(direction));
			}
			if (dstRow < 0 || dstRow >= BattleSceneContainer.Instance.BattleMap.Rows || dstCol < 0 || dstCol >= BattleSceneContainer.Instance.BattleMap.Cols)
				throw new InvalidOperationException("Move out of map.");
			Grid dstGrid = BattleSceneContainer.Instance.BattleMap[dstRow, dstCol];
			bool dstHighland = stairway ^ this.IsHighland;
			IReadOnlyList<GridObject> land = dstHighland ? dstGrid.Highland : dstGrid.Lowland;
			if (land.Count <= 0 || land[land.Count - 1].IsObstacle) throw new InvalidOperationException("Cannot reach the place.");
			int leftMovePoint = this.MovePoint;
			if (stairway) {
				LadderObject ladderObject = this.GridRef.GetLadder(direction);
				leftMovePoint -= ladderObject.Stagnate;
			}
			GridObject terrian = null;
			for (int i = land.Count - 1; i >= 0; --i) {
				if (land[i].IsTerrain) {
					terrian = land[i];
					break;
				}
			}
			if (terrian == null) throw new InvalidOperationException("Cannot reach the place.");
			leftMovePoint -= terrian.Stagnate;
			if (leftMovePoint < 0) throw new InvalidOperationException("Move points are not enough.");
			this.MovePoint = leftMovePoint;
			BattleSceneContainer.Instance.BattleMap.MoveStack(srcRow, srcCol, this.IsHighland, dstRow, dstCol, dstHighland);
			_movepathCache = null;
			foreach (Player player in Game.Players) {
				player.Client.BattleScene.DisplayActableObjectMove(this, direction, stairway);
			}
			Game.DM.Client.BattleScene.DisplayActableObjectMove(this, direction, stairway);
		}

		public void MoveTo(int dstRow, int dstCol, bool dstHighland) {
			var path = GetReachablePlaceList();
			var dst = MarkedReachablePlace(path, dstRow, dstCol, dstHighland);
			if (dst == null) throw new InvalidOperationException("Cannot reach the place.");
			List<BattleMapDirection> moveDirections = new List<BattleMapDirection>();
			List<bool> moveStairways = new List<bool>();
			while (dst.prevPlace != null) {
				var prevPlace = dst.prevPlace;
				int direction = 0;
				if (prevPlace.row - dst.row == 1 && prevPlace.col == dst.col) direction = (int)BattleMapDirection.NEGATIVE_ROW;
				else if (prevPlace.row - dst.row == -1 && prevPlace.col == dst.col) direction = (int)BattleMapDirection.POSITIVE_ROW;
				else if (prevPlace.row == dst.row && prevPlace.col - dst.col == 1) direction = (int)BattleMapDirection.NEGATIVE_COL;
				else if (prevPlace.row == dst.row && prevPlace.col - dst.col == -1) direction = (int)BattleMapDirection.POSITIVE_COL;
				Debug.Assert(direction != 0, "GetReachablePlaceList() returns a invalid list.");
				bool stairway = prevPlace.highland ^ dst.highland;
				moveDirections.Add((BattleMapDirection)direction);
				moveStairways.Add(stairway);
				dst = prevPlace;
			}
			moveDirections.Reverse();
			moveStairways.Reverse();
			for (int i = 0; i < moveDirections.Count; ++i) {
				Move(moveDirections[i], moveStairways[i]);
			}
		}

		public void UseSkill(SkillType skillType, SkillChecker.CharacterAction action, int centerRow, int centerCol, IEnumerable<GridObject> targets) {
			if (skillType == null) throw new ArgumentNullException(nameof(skillType));
			if (centerRow >= BattleSceneContainer.Instance.BattleMap.Rows || centerRow < 0)
				throw new ArgumentOutOfRangeException(nameof(centerRow));
			if (centerCol >= BattleSceneContainer.Instance.BattleMap.Cols || centerCol < 0)
				throw new ArgumentOutOfRangeException(nameof(centerCol));

			SkillProperty skillProperty = this.CharacterRef.GetSkillProperty(skillType);
			if (skillProperty.islinearUse) {
				do {
					if (centerRow == this.GridRef.PosRow && centerCol == this.GridRef.PosCol && skillProperty.useRange.InRange(0)) break;
					if ((skillProperty.linearUseDirection & BattleMapDirection.POSITIVE_ROW) != 0 && centerCol == this.GridRef.PosCol && skillProperty.useRange.InRange(centerRow - this.GridRef.PosRow)) break;
					if ((skillProperty.linearUseDirection & BattleMapDirection.NEGATIVE_ROW) != 0 && centerCol == this.GridRef.PosCol && skillProperty.useRange.InRange(this.GridRef.PosRow - centerRow)) break;
					if ((skillProperty.linearUseDirection & BattleMapDirection.POSITIVE_COL) != 0 && centerRow == this.GridRef.PosRow && skillProperty.useRange.InRange(centerCol - this.GridRef.PosCol)) break;
					if ((skillProperty.linearUseDirection & BattleMapDirection.NEGATIVE_COL) != 0 && centerRow == this.GridRef.PosRow && skillProperty.useRange.InRange(this.GridRef.PosCol - centerCol)) break;
					throw new InvalidOperationException("Cannot use this skill at the specified position.");
				} while (false);
			} else if (skillProperty.useRange.OutOfRange(Math.Abs(centerRow - this.GridRef.PosRow) + Math.Abs(centerCol - this.GridRef.PosCol))) throw new InvalidOperationException("Cannot use this skill at the specified position.");
			if (skillProperty.targetCount == -1) {
				List<GridObject> areaTargets = new List<GridObject>();
				if (skillProperty.islinearAffect) {
					if (skillProperty.affectRange.InRange(0)) {
						areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[centerRow, centerCol].Lowland);
						areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[centerRow, centerCol].Highland);
					}
					if ((skillProperty.linearAffectDirection & BattleMapDirection.POSITIVE_ROW) != 0) {
						for (int row = centerRow + 1; row < BattleSceneContainer.Instance.BattleMap.Rows; ++row) {
							if (skillProperty.affectRange.InRange(row - centerRow)) {
								areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[row, centerCol].Lowland);
								areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[row, centerCol].Highland);
							}
						}
					}
					if ((skillProperty.linearAffectDirection & BattleMapDirection.NEGATIVE_ROW) != 0) {
						for (int row = centerRow - 1; row >= 0; --row) {
							if (skillProperty.affectRange.InRange(centerRow - row)) {
								areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[row, centerCol].Lowland);
								areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[row, centerCol].Highland);
							}
						}
					}
					if ((skillProperty.linearAffectDirection & BattleMapDirection.POSITIVE_COL) != 0) {
						for (int col = centerCol + 1; col < BattleSceneContainer.Instance.BattleMap.Cols; ++col) {
							if (skillProperty.affectRange.InRange(col - centerCol)) {
								areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[centerRow, col].Lowland);
								areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[centerRow, col].Highland);
							}
						}
					}
					if ((skillProperty.linearAffectDirection & BattleMapDirection.NEGATIVE_COL) != 0) {
						for (int col = centerCol - 1; col >= 0; --col) {
							if (skillProperty.affectRange.InRange(centerCol - col)) {
								areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[centerRow, col].Lowland);
								areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[centerRow, col].Highland);
							}
						}
					}
				} else {
					for (int row = 0; row < BattleSceneContainer.Instance.BattleMap.Rows; ++row) {
						for (int col = 0; col < BattleSceneContainer.Instance.BattleMap.Cols; ++col) {
							if (skillProperty.affectRange.InRange(Math.Abs(centerRow - centerRow) + Math.Abs(centerCol - centerCol))) {
								areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[row, col].Lowland);
								areaTargets.AddRange(BattleSceneContainer.Instance.BattleMap[row, col].Highland);
							}
						}
					}
				}
				BattleSceneContainer.Instance.StartCheck(this, areaTargets, action, skillType);
			} else {
				if (targets == null) throw new ArgumentNullException(nameof(targets));
				int count = 0;
				foreach (GridObject target in targets) {
					if (target == null) throw new ArgumentNullException(nameof(target));
					int row = target.GridRef.PosRow;
					int col = target.GridRef.PosCol;
					if (skillProperty.islinearAffect) {
						do {
							if ((skillProperty.linearAffectDirection & BattleMapDirection.POSITIVE_ROW) != 0 && col == centerCol && skillProperty.affectRange.InRange(row - centerRow)) break;
							if ((skillProperty.linearAffectDirection & BattleMapDirection.NEGATIVE_ROW) != 0 && col == centerCol && skillProperty.affectRange.InRange(centerRow - row)) break;
							if ((skillProperty.linearAffectDirection & BattleMapDirection.POSITIVE_COL) != 0 && row == centerRow && skillProperty.affectRange.InRange(col - centerCol)) break;
							if ((skillProperty.linearAffectDirection & BattleMapDirection.NEGATIVE_COL) != 0 && row == centerRow && skillProperty.affectRange.InRange(centerCol - col)) break;
							throw new InvalidOperationException("Target is not in range!");
						} while (false);
					} else if (skillProperty.affectRange.OutOfRange(Math.Abs(row - centerRow) + Math.Abs(col - centerCol))) throw new InvalidOperationException("Target is not in range!");
					++count;
				}
				if (count > skillProperty.targetCount) throw new InvalidOperationException("Targets count is more than limit!");
				BattleSceneContainer.Instance.StartCheck(this, targets, action, skillType);
			}
		}

		public void UseStunt(Stunt stunt, SkillChecker.CharacterAction action, int centerRow, int centerCol, IEnumerable<GridObject> targets) {
			if (stunt == null) throw new ArgumentNullException(nameof(stunt));
			if (stunt.Belong != this.CharacterRef) throw new ArgumentException("This stunt is not belong to the character.", nameof(stunt));
			if (stunt.NeedDMCheck)
				Game.DM.DMClient.DMCheckDialog.RequestCheck(this.CharacterRef.Controller,
					SkillChecker.Instance.Passive.Name + "对" + SkillChecker.Instance.Initiative.Name + "使用" + stunt.Name + ",可以吗？",
					result => {
						if (result) {
							//_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStuntComplete();
							stunt.InitiativeEffect.DoAction();
						} else {
							//_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStuntFailure("DM拒绝了你选择的特技");
						}
					});
			else {
				//_currentPassive.CharacterRef.Controller.Client.BattleScene.NotifyPassiveSelectSkillOrStuntComplete();
				stunt.InitiativeEffect.DoAction();
			}
		}

		public override IJSContext GetContext() {
			return _apiObj;
		}
	}

	public sealed class Grid : IJSContextProvider {
		#region Javascript API class
		private sealed class JSAPI : IJSAPI<Grid> {
			private readonly Grid _outer;

			public JSAPI(Grid outer) {
				_outer = outer;
			}

			public Grid Origin(JSContextHelper proof) {
				try {
					if (proof == JSContextHelper.Instance) {
						return _outer;
					}
					return null;
				} catch (Exception) {
					return null;
				}
			}
		}
		#endregion
		private readonly JSAPI _apiObj;

		private readonly List<GridObject> _highland;
		private readonly List<GridObject> _lowland;
		private LadderObject _positiveRowLadder = null;
		private LadderObject _positiveColLadder = null;
		private LadderObject _negativeRowLadder = null;
		private LadderObject _negativeColLadder = null;
		private bool _isMiddleLand = false;
		private readonly int _posRow;
		private readonly int _posCol;

		public IReadOnlyList<GridObject> Highland => _highland;
		public IReadOnlyList<GridObject> Lowland => _lowland;
		public LadderObject PositiveRowLadder => _positiveRowLadder;
		public LadderObject PositiveColLadder => _positiveColLadder;
		public LadderObject NegativeRowLadder => _negativeRowLadder;
		public LadderObject NegativeColLadder => _negativeColLadder;
		public bool IsMiddleLand { get => _isMiddleLand; set => _isMiddleLand = value; }
		public int PosRow => _posRow;
		public int PosCol => _posCol;

		public Grid(int row, int col) {
			_posRow = row;
			_posCol = col;
			_highland = new List<GridObject>();
			_lowland = new List<GridObject>();
			_apiObj = new JSAPI(this);
		}

		public LadderObject GetLadder(BattleMapDirection direction) {
			switch (direction) {
				case BattleMapDirection.POSITIVE_ROW:
					return _positiveRowLadder;
				case BattleMapDirection.POSITIVE_COL:
					return _positiveColLadder;
				case BattleMapDirection.NEGATIVE_ROW:
					return _negativeRowLadder;
				case BattleMapDirection.NEGATIVE_COL:
					return _negativeColLadder;
				default:
					return null;
			}
		}

		public void SetLadderRef(BattleMapDirection direction, LadderObject ladderRef) {
			switch (direction) {
				case BattleMapDirection.POSITIVE_ROW:
					_positiveRowLadder = ladderRef;
					break;
				case BattleMapDirection.POSITIVE_COL:
					_positiveColLadder = ladderRef;
					break;
				case BattleMapDirection.NEGATIVE_ROW:
					_negativeRowLadder = ladderRef;
					break;
				case BattleMapDirection.NEGATIVE_COL:
					_negativeColLadder = ladderRef;
					break;
				default:
					break;
			}
		}

		public IJSContext GetContext() {
			return _apiObj;
		}

		public void SetContext(IJSContext context) { }
	}

	public sealed class ReachablePlace {
		public ReachablePlace prevPlace;
		public int row;
		public int col;
		public bool highland;
		public int leftMovePoint;
	}

	public sealed class BattleMap {
		private Grid[,] _grids;
		private int _rows;
		private int _cols;

		public int Rows => _rows;
		public int Cols => _cols;
		public Grid this[int row, int col] => _grids[row, col];

		public BattleMap() {
			this.Reset(0, 0);
		}

		public void Reset(int rows, int cols) {
			_rows = rows;
			_cols = cols;
			_grids = new Grid[_rows, _cols];
			for (int i = 0; i < _rows; ++i) {
				for (int j = 0; j < _cols; ++j) {
					_grids[i, j] = new Grid(i, j);
				}
			}
		}

		public void MoveStack(int srcRow, int srcCol, bool srcHighland, int dstRow, int dstCol, bool dstHighland, int count = 1) {
			Grid srcGrid = _grids[srcRow, srcCol];
			Grid dstGrid = _grids[dstRow, dstCol];
			List<GridObject> srcLand, dstLand;
			if (srcHighland) srcLand = (List<GridObject>)srcGrid.Highland;
			else srcLand = (List<GridObject>)srcGrid.Lowland;
			if (dstHighland) dstLand = (List<GridObject>)dstGrid.Highland;
			else dstLand = (List<GridObject>)dstGrid.Lowland;
			int lastIndex = dstLand.Count;
			for (int i = srcLand.Count - 1; srcLand.Count - i <= count; --i) {
				GridObject gridObject = srcLand[i];
				gridObject.GridRef = dstGrid;
				gridObject.IsHighland = dstHighland;
				dstLand.Insert(lastIndex, gridObject);
				srcLand.RemoveAt(i);
			}
		}

	}
}