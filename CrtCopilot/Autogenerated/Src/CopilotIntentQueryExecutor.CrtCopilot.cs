namespace Creatio.Copilot
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Creatio.Copilot.Metadata;
	using Creatio.FeatureToggling;
	using Terrasoft.AppFeatures;
	using Terrasoft.Common;
	using Terrasoft.Core;
	using Terrasoft.Core.Entities;
	using Terrasoft.Core.Factories;
	using Terrasoft.Core.Store;

	#region Class: CopilotIntentQueryExecutor

	[DefaultBinding(typeof(IEntityQueryExecutor), Name = "CopilotIntentQueryExecutor")]
	public class CopilotIntentQueryExecutor : BaseQueryExecutor, IEntityQueryExecutor
	{

		#region Fields: Private

		private readonly string _entitySchemaName = "CopilotIntent";
		private CopilotIntentSchemaManager _copilotIntentSchemaManager;
		private static readonly string _commonTypeColor = "#7848EE";
		private static readonly string _defaultTypeColor = "#22AC14";
		private static readonly Guid _commonIntentTypeId = Guid.Parse("6D940B75-21C8-4A90-89AB-9867E6E4A045");
		private static readonly Guid _defaultIntentTypeId = Guid.Parse("35F3B644-4FA3-4D1E-8E62-5C3FDC4D3E52");
		private readonly Dictionary<CopilotIntentType, (Guid Id, string Color)> _intentColorMap =
			new Dictionary<CopilotIntentType, (Guid Id, string Color)> {
				{ CopilotIntentType.Common, (_commonIntentTypeId, _commonTypeColor) },
				{ CopilotIntentType.Default, (_defaultIntentTypeId, _defaultTypeColor) }
			};

		#endregion

		#region Constructors: Public

		public CopilotIntentQueryExecutor(UserConnection userConnection)
			: base(userConnection, "CopilotIntent") { }

		#endregion

		#region Properties: Public

		public CopilotIntentSchemaManager CopilotIntentSchemaManager =>
			_copilotIntentSchemaManager ?? (_copilotIntentSchemaManager =
				(CopilotIntentSchemaManager)UserConnection.GetSchemaManager(nameof(CopilotIntentSchemaManager)));

		#endregion

		#region Properties: Private

		private EntitySchema IntentEntitySchema =>
			UserConnection.EntitySchemaManager.GetInstanceByName(_entitySchemaName);

		#endregion

		#region Methods: Private

		private void GetIntentUIdByFilter(QueryFilterInfo filter, out Guid intentUId) {
			GetIsPrimaryColumnValueFilter(filter, out intentUId);
		}

		private EntitySchemaQuery GetCopilotIntentLookupESQ(string lookupName) {
			var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, lookupName) {
				PrimaryQueryColumn = {
					IsAlwaysSelect = true
				},
				UseAdminRights = false
			};
			esq.AddColumn("Name");
			esq.AddColumn("Code");
			esq.Cache = UserConnection.SessionCache.WithLocalCaching("CopilotIntentStatusCache");
			esq.CacheItemName = $"{lookupName}CacheItem";
			return esq;
		}

		private Entity GetIntentStatus(string code) {
			return GetCopilotIntentLookupESQ("CopilotIntentStatus")
				.GetEntityCollection(UserConnection)
				.FirstOrDefault(status => status.GetTypedColumnValue<string>("Code") == code);
		}

		private Entity GetIntentMode(CopilotIntentBehavior behavior) {
			return GetCopilotIntentLookupESQ("CopilotIntentMode")
				.GetEntityCollection(UserConnection)
				.FirstOrDefault(mode =>
					mode.GetTypedColumnValue<string>("Code") == (behavior.SkipForChat ? "API" : "Chat"));
		}

		private Entity GetIntentEntityFromManagerItem(ISchemaManagerItem<CopilotIntentSchema> schemaManagerItem) {
			CopilotIntentSchema intentSchema = schemaManagerItem.Instance;
			Entity entity = IntentEntitySchema.CreateEntity(UserConnection);
			entity.LoadColumnValue("Id", intentSchema.UId);
			entity.LoadColumnValue("Code", intentSchema.Name);
			entity.LoadColumnValue("Description", intentSchema.IntentDescription ?? intentSchema.Description);
			entity.LoadColumnValue("Name", intentSchema.Caption);
			entity.LoadColumnValue("Prompt", intentSchema.Prompt);
			Entity status = GetIntentStatus(intentSchema.Status.ToString());
			if (status != null) {
				entity.LoadColumnValue("StatusId", status.GetTypedColumnValue<Guid>("Id"));
				entity.LoadColumnValue("StatusName", status.GetTypedColumnValue<string>("Name"));
			}
			Entity mode = GetIntentMode(intentSchema.Behavior);
			if (mode != null) {
				entity.LoadColumnValue("ModeId", mode.GetTypedColumnValue<Guid>("Id"));
				entity.LoadColumnValue("ModeName", mode.GetTypedColumnValue<string>("Name"));
			}
			if (_intentColorMap.TryGetValue(intentSchema.Type, out (Guid Id, string Color) map)) {
				entity.LoadColumnValue("TypeId", map.Id);
				entity.LoadColumnValue("TypePrimaryColor", map.Color);
				entity.LoadColumnValue("TypeName", intentSchema.Type.ToString());
			}
			return entity;
		}

		private IEnumerable<Entity> GetAllIntentEntities(
				IEnumerable<ISchemaManagerItem<CopilotIntentSchema>> managerItems) {
			var entities = new List<Entity>();
			foreach (ISchemaManagerItem<CopilotIntentSchema> schemaManagerItem in managerItems) {
				entities.Add(GetIntentEntityFromManagerItem(schemaManagerItem));
			}
			return entities;
		}

		private bool GetIsLookupColumnFilter(string columnPath, out EntitySchemaColumn sourceColumn,
				out string targetSchemaColumn) {
			string[] columnParts = columnPath.Split(new [] { '.' }, StringSplitOptions.RemoveEmptyEntries);
			if (columnParts.Length != 2) {
				sourceColumn = null;
				targetSchemaColumn = null;
				return false;
			}
			EntitySchema entitySchema = UserConnection.EntitySchemaManager.GetInstanceByName("CopilotIntent");
			sourceColumn = entitySchema.Columns.FindByName(columnParts.First());
			targetSchemaColumn = columnParts.Last();
			return sourceColumn != null && !sourceColumn.ReferenceSchema.IsVirtual;
		}

		private IEnumerable<Entity> FilterByLookupColumnReference(List<object> parameterValues,
				EntitySchemaColumn lookupColumn, string targetColumnName, IEnumerable<Entity> entities) {
			string lookupSchemaName = lookupColumn.ReferenceSchema.Name;
			EntitySchemaQuery esq = GetCopilotIntentLookupESQ(lookupSchemaName);
			EntityCollection lookupEntities = esq.GetEntityCollection(UserConnection);
			EntitySchemaColumn targetColumn = esq.RootSchema.Columns.FindByName(targetColumnName);
			entities = entities.Where(entity => {
				Guid lookupId = entity.GetTypedColumnValue<Guid>(lookupColumn.ColumnValueName);
				Entity lookupEntity = lookupEntities.FirstOrDefault(lookup => lookup.GetTypedColumnValue<Guid>("Id") == lookupId);
				if (lookupEntity == null) {
					return false;
				}
				var targetColumnValue = lookupEntity.GetTypedColumnValue<string>(targetColumn.ColumnValueName);
				return parameterValues.Any(p => targetColumnValue?.ToLower().Contains(p.ToString().ToLower()) ?? true);
			});
			return entities;
		}

		private IEnumerable<Entity> GetEntitiesBySingleFilter(CompareColumnWithValueFilter compareFilter,
				IEnumerable<Entity> entities) {
			 switch (compareFilter.ColumnPath) {
				case "Name":
				case "Description": {
					entities = entities
						.Where(entity => compareFilter.ParameterValues
							.Any(p => entity.GetTypedColumnValue<string>(compareFilter.ColumnPath).ToLower()
								.Contains(p.ToString().ToLower())));
					break;
				}
				case "StatusId": {
					entities = entities
						.Where(entity => compareFilter.ParameterValues
							.Contains(entity.GetTypedColumnValue<Guid>("StatusId")));
					break;
				}
				case "Id": {
					GetIntentUIdByFilter(compareFilter, out Guid intentUId);
					if (intentUId.IsNotEmpty()) {
						entities = entities.Where(entity => entity.GetTypedColumnValue<Guid>("Id") == intentUId);
					}
					break;
				}
				default:
					break;
			}
			if (GetIsLookupColumnFilter(compareFilter.ColumnPath, out EntitySchemaColumn sourceColumn,
					out string targetSchemaColumnName)) {
				return FilterByLookupColumnReference(compareFilter.ParameterValues, sourceColumn,
					targetSchemaColumnName, entities);
			}
			return entities;
		}

		private IEnumerable<Entity> GetEntitiesByFiltersCollection(FilterCollection filtersCollection,
				IEnumerable<Entity> entities) {
			if (filtersCollection.Filters.Count == 0) {
				return entities;
			}
			if (filtersCollection.LogicalOperation == LogicalOperationStrict.Or) {
				var resultEntities = Enumerable.Empty<Entity>();
				foreach (var filter in filtersCollection.Filters) {
					if (!(filter is CompareColumnWithValueFilter columnFilter)) {
						continue;
					}
					var filterredEntities = GetEntitiesBySingleFilter(columnFilter, entities);
					resultEntities = resultEntities.Union(filterredEntities);
				}
				return resultEntities;
			} else {
				foreach (var filter in filtersCollection.Filters) {
					if (!(filter is CompareColumnWithValueFilter columnFilter)) {
						continue;
					}
					entities = GetEntitiesBySingleFilter(columnFilter, entities);
				}
				return entities;
			}
		}

		private IEnumerable<Entity> GetIntentEntityFromManagerItemByFilters(
			IEnumerable<ISchemaManagerItem<CopilotIntentSchema>> managerItems, QueryFilterInfo filterInfo) {
			if (filterInfo is CompareColumnWithValueFilter compareFilter) {
				GetIntentUIdByFilter(filterInfo, out Guid intentUId);
				if (intentUId.IsNotEmpty()) {
					ISchemaManagerItem<CopilotIntentSchema> schemaManagerItem =
						managerItems.FirstOrDefault(item => item.UId == intentUId);
					if (schemaManagerItem != null) {
						return new Entity[] { GetIntentEntityFromManagerItem(schemaManagerItem) };
					}
				}
				var entities = GetAllIntentEntities(managerItems);
				return GetEntitiesBySingleFilter(compareFilter, entities);
			}
			if (filterInfo is FilterCollection filterCollection) {
				var entities = GetAllIntentEntities(managerItems);
				return GetEntitiesByFiltersCollection(filterCollection, entities);
			}
			return GetAllIntentEntities(managerItems);
		}

		private IEnumerable<ISchemaManagerItem<CopilotIntentSchema>> GetIntentManagerItems() {
			IEnumerable<ISchemaManagerItem<CopilotIntentSchema>> items = CopilotIntentSchemaManager.GetItems();
			Func<ISchemaManagerItem<CopilotIntentSchema>, bool> isCommonIntent = item => {
				var property = item.ExtraProperties.FindByName("Type")?.Value?.ToString();
				return Enum.TryParse(property, out CopilotIntentType intentType) &&
					intentType.Equals(CopilotIntentType.Common);
			};
			return Features.GetIsEnabled<Terrasoft.Configuration.GenAI.GenAIFeatures.ShowDefaultSkill>()
				? items.OrderBy(isCommonIntent)
				: items.Where(isCommonIntent);
		}

		#endregion

		#region Methods: Public

		/// <summary>
		/// Returns intent data.
		/// </summary>
		/// <param name="esq">Filters.</param>
		/// <returns>Intent data collection.</returns>
		public EntityCollection GetEntityCollection(EntitySchemaQuery esq) {
			QueryFilterInfo filterInfo = esq.Filters.ParseFilters();
			var collection = new EntityCollection(UserConnection, EntitySchema);
			IEnumerable<ISchemaManagerItem<CopilotIntentSchema>> intentManagerItems = GetIntentManagerItems();
			IEnumerable<Entity> entities = GetIntentEntityFromManagerItemByFilters(intentManagerItems, filterInfo);
			if (esq.RowCount > 0) {
				entities = entities.Skip(esq.SkipRowCount).Take(esq.RowCount);
			}
			collection.AddRange(entities);
			return collection;
		}

		#endregion

	}

	#endregion

}

