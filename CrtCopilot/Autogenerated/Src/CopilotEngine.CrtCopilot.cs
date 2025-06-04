namespace Creatio.Copilot
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Security;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;
	using Creatio.Copilot.Actions;
	using Creatio.Copilot.Metadata;
	using Creatio.FeatureToggling;
	using Terrasoft.Common;
	using Terrasoft.Configuration.GenAI;
	using Terrasoft.Core;
	using Terrasoft.Core.Applications.GenAI;
	using Terrasoft.Core.DB;
	using Terrasoft.Core.Factories;
	using Terrasoft.Enrichment.Interfaces.ChatCompletion;
	using Terrasoft.Enrichment.Interfaces.ChatCompletion.Requests;
	using Terrasoft.Enrichment.Interfaces.ChatCompletion.Responses;
	using global::Common.Logging;
	using Newtonsoft.Json;
	using Terrasoft.Common.Threading;
	using Terrasoft.Core.Entities;
	using Terrasoft.Core.Security;
	using Terrasoft.Core.Store;

	[DefaultBinding(typeof(ICopilotEngine))]
	internal class CopilotEngine : ICopilotEngine
	{

		#region Constants: Private

		private const string CanDevelopCopilotIntentsOperation = "CanDevelopAISkills";
		private const string CanRunCopilotOperation = "CanRunCreatioAI";
		private const string CanRunCopilotApiOperation = "CanRunCreatioAIApi";
		private const string ApiSystemPromptCode = "ApiSystem";
		private const string SystemPromptCode = "System";

		#endregion

		#region Fields: Private

		private static readonly CreatePromptOptions _alternativeNameInDescriptionChatPromptOptions = new CreatePromptOptions {
			AdditionalDirections = {
				{
					"## Global settings",
					new[] {
						"* Note that each function's and property's description may contain an alternative name. You should take into account the alternative name when choosing the function to call or property to refer. The alternative name may be specified in any language. You should consider the function's or property's alternative name regardless of the language in which it is specified. If you find a match for a function or a parameter using its alternative name you strictly must refer to it using its originally specified name when requesting a tool call. Do not disclose the alternative name as a part of the function description and treat it as a separate property.",
					}
				}
			}
		};

		private readonly ILog _log = LogManager.GetLogger("Copilot");
		private readonly UserConnection _userConnection;
		private readonly IGenAICompletionServiceProxy _completionService;
		private readonly ICopilotSessionManager _sessionManager;
		private readonly ICopilotRequestLogger _requestLogger;
		private readonly ICopilotOutputParametersHandler _outputParametersHandler;
		private readonly ICopilotMsgChannelSender _copilotMsgChannelSender;
		private readonly ICopilotContextBuilder _contextBuilder;
		private readonly ICopilotToolProcessor _toolProcessor;
		private readonly ICopilotPromptFactory _promptFactory;
		private readonly ICopilotPromptVariableResolver _variableResolver;
		private readonly ICopilotHyperlinkUtils _hyperlinkUtils;

		#endregion

		#region Constructors: Public

		/// <summary>
		/// Initializes a new instance of the <see cref="CopilotEngine"/>
		/// </summary>
		/// <param name="userConnection">User connection.</param>
		/// <param name="completionService">GenAI Completion service.</param>
		/// <param name="copilotSessionManager">Copilot session manager.</param>
		/// <param name="copilotMsgChannelSender">Copilot message sender.</param>
		/// <param name="contextBuilder">Copilot context builder.</param>
		/// <param name="toolProcessor">Copilot tool processor.</param>
		/// <param name="requestLogger">Copilot request logger.</param>
		/// <param name="outputParametersHandler">Copilot output parameters handler.</param>
		/// <param name="promptFactory">Copilot prompt factory.</param>
		/// <param name="variableResolverFactory">Copilot prompt variable resolver factory.</param>
		/// <param name="hyperlinkUtils">An instance of <see cref="ICopilotHyperlinkUtils"/>
		/// used for handling hyperlink-related utilities.</param>
		public CopilotEngine(UserConnection userConnection, IGenAICompletionServiceProxy completionService,
				ICopilotSessionManager copilotSessionManager, ICopilotMsgChannelSender copilotMsgChannelSender,
				ICopilotContextBuilder contextBuilder, ICopilotToolProcessor toolProcessor,
				ICopilotRequestLogger requestLogger, ICopilotOutputParametersHandler outputParametersHandler,
				ICopilotPromptFactory promptFactory, ICopilotPromptVariableResolverFactory variableResolverFactory,
				ICopilotHyperlinkUtils hyperlinkUtils) {
			_userConnection = userConnection;
			_completionService = completionService;
			_sessionManager = copilotSessionManager;
			_copilotMsgChannelSender = copilotMsgChannelSender;
			_contextBuilder = contextBuilder;
			_toolProcessor = toolProcessor;
			_requestLogger = requestLogger;
			_outputParametersHandler = outputParametersHandler;
			_promptFactory = promptFactory;
			_variableResolver = variableResolverFactory.Create();
			_hyperlinkUtils = hyperlinkUtils;
		}

		#endregion

		#region Properties: Private

		private CopilotIntentSchema _systemIntent;

		private CopilotIntentSchema SystemIntent =>
			_systemIntent ?? (_systemIntent = SkillSchemaService.FindSystemSkill());

		private string PromptToRemoveInvalidLinks => "Rewrite the provided text, removing invalid links that " +
			"literally and strictly look like \"" + _hyperlinkUtils.InvalidLinkMarker + "\". If the message " +
			"does not contain other links except \"" + _hyperlinkUtils.InvalidLinkMarker + "\" please tell " +
			"that without mentioning the \"" + _hyperlinkUtils.InvalidLinkMarker + "\". All other links are " +
			"valid. Remove invalid links naturally, without additional comments, but save the idea and structure. " +
			"If the invalid link contains alternative text or markdown format remove them as well.";

		#endregion

		#region Properties: Public

		private ISkillSchemaService _skillSchemaService;

		/// <summary>
		/// Gets or sets the instance of <see cref="ISkillSchemaService"/>.
		/// </summary>
		/// <value>
		/// The <see cref="ISkillSchemaService"/> instance used to interact with skill schema service.
		/// </value>
		public ISkillSchemaService SkillSchemaService {
			get {
				if (_skillSchemaService != null) {
					return _skillSchemaService;
				}
				return _skillSchemaService = _userConnection.GetSkillSchemaService();
			}
			set => _skillSchemaService = value;
		}

		#endregion

		#region Methods: Private

		private CopilotSession CreateSession() {
			bool descriptionContainsAlternativeName =
				Features.GetIsEnabled<Terrasoft.Configuration.GenAI.GenAIFeatures.AddCaptionToDescription>();
			string systemPrompt = GetCopilotPrompt(SystemPromptCode,
				_promptFactory.CreateSystemPrompt(SystemPromptTarget.Chat,
					descriptionContainsAlternativeName
						? _alternativeNameInDescriptionChatPromptOptions
						: null));
			CopilotMessage message = CopilotMessage.FromSystem(systemPrompt);
			message.IsFromSystemPrompt = true;
			var messages = new List<CopilotMessage> { message };
			var session = new CopilotSession(_userConnection.CurrentUser.Id, messages, null);
			_sessionManager.Add(session);
			return session;
		}

		private void AdjustSessionSystemIntentPrompt(CopilotSession copilotSession) {
			if (copilotSession.CurrentIntentId.IsNullOrEmpty()) {
				return;
			}
			if (copilotSession.Messages.Any(copilotMessage => copilotMessage.IsFromSystemIntent)) {
				return;
			}
			string baseApplicationUrl = _hyperlinkUtils.GetBaseApplicationUrl();
			var parameters = new Dictionary<string, object> {{ "BaseAppUrl", baseApplicationUrl }};
			string systemIntentPrompt = GenerateIntentPrompt(SystemIntent, parameters);
			CopilotMessage message = CopilotMessage.FromSystem(systemIntentPrompt);
			message.IsFromSystemIntent = true;
			copilotSession.AddMessage(message);
		}

		private Guid SaveRequestInfo(DateTime? start, DateTime? end, UsageResponse usage, string error, bool isFailed) {
			start = start ?? DateTime.Now;
			end = end ?? DateTime.Now;
			var duration = (long)(end - start).Value.TotalMilliseconds;
			var requestInfo = new CopilotRequestInfo {
				StartDate = start.Value,
				Error = error,
				TotalTokens = usage?.TotalTokens ?? 0,
				PromptTokens = usage?.PromptTokens ?? 0,
				CompletionTokens = usage?.CompletionTokens,
				Duration = duration,
				IsFailed = isFailed
			};
			return _requestLogger.SaveCopilotRequest(requestInfo);
		}

		private string RemoveInvalidLinks(string content) {
			try {
				ChatCompletionRequest rewriteRequest = CreateCompletionRequest(PromptToRemoveInvalidLinks, content);
				var (_, _, response) = ProcessCompletionRequest(rewriteRequest)
					.GetAwaiter()
					.GetResult();
				List<CopilotMessage> responseMessages = GetAssistantMessagesWithoutToolCalls(response);
				return responseMessages.Select(x => x.Content).ConcatIfNotEmpty(string.Empty);
			} catch (Exception e) {
				_log.Error("Error occurred while removing invalid links", e);
				return content;
			}
		}

		private void HandleCompletionResponse(ChatCompletionResponse completionResponse, CopilotSession session) {
			if (completionResponse?.Choices == null) {
				return;
			}
			List<CopilotMessage> assistantMessages = GetAssistantMessagesWithoutToolCalls(completionResponse);
			assistantMessages.ForEach(message => {
				if (!_hyperlinkUtils.TryMarkInvalidLinks(session, message.Content, out string markedContent)) {
					return;
				}
				message.Content = RemoveInvalidLinks(markedContent);
			});
			session.AddMessages(assistantMessages);
			SendMessagesToClient(session);
		}

		private void SendMessagesToClient(CopilotSession copilotSession) {
			List<BaseCopilotMessage> messagesToSend = copilotSession.Messages.Where(message => !message.IsSentToClient)
				.Cast<BaseCopilotMessage>().ToList();
			if (messagesToSend.IsNullOrEmpty()) {
				return;
			}
			var copilotChatPart = new CopilotChatPart(messagesToSend, copilotSession);
			_copilotMsgChannelSender.SendMessages(copilotChatPart);
			messagesToSend.ForEach(message => message.IsSentToClient = true);
		}

		private static List<CopilotMessage> GetAssistantMessagesWithoutToolCalls(
				ChatCompletionResponse completionResponse) {
			List<CopilotMessage> assistantMessages = GetResponsesWithMessage(completionResponse)
				.Select(response => new CopilotMessage(response.Message, skipToolCalls: true))
				.ToList();
			return assistantMessages;
		}

		private static void SetErrorResponse(CopilotIntentCallResult response, string errorMessage,
				IntentCallStatus status = IntentCallStatus.FailedToExecute) {
			response.ErrorMessage = errorMessage;
			response.Status = status;
		}

		private static string GetCopilotMessage(ChatCompletionResponse completionResponse) {
			IEnumerable<string> messages = GetResponsesWithMessage(completionResponse)
				.Select(response => response.Message.Content);
			return string.Join(" ", messages);
		}

		private static IEnumerable<ChatChoiceResponse> GetResponsesWithMessage(
				ChatCompletionResponse completionResponse) {
			return completionResponse.Choices.Where(response => response.Message.Content.IsNotNullOrEmpty());
		}

		private static IEnumerable<string> FilterActiveIntents(string[] names,
				IEnumerable<CopilotIntentSchema> activeIntents) {
			HashSet<string> allActiveIntentNames = activeIntents.Select(intent => intent.Name.ToLower()).ToHashSet();
			return names.Where(name => allActiveIntentNames.Contains(name.ToLower()));
		}

		private void HandleToolCallsCompleted(List<CopilotMessage> toolMessages, CopilotSession session,
				CopilotContext copilotContext = null) {
			if (toolMessages.IsNullOrEmpty()) {
				return;
			}
			AdjustSessionSystemIntentPrompt(session);
			session.AddMessages(toolMessages);
			UpdateContext(copilotContext, session);
			SendSession(session, copilotContext);
		}

		private bool CanRunCopilotApi() {
			if (Features.GetIsDisabled<GenAIFeatures.EnableStandaloneApi>()) {
				return false;
			}
			return _userConnection.DBSecurityEngine.GetCanExecuteOperation(CanRunCopilotApiOperation);
		}

		private bool CanUseCopilotChat() {
			return _userConnection.DBSecurityEngine.GetCanExecuteOperation(CanRunCopilotOperation);
		}

		private IEnumerable<CopilotIntentSchema> FindCommonIntentsForChat(Guid? currentIntentId) {
			IEnumerable<CopilotIntentSchema> items = SkillSchemaService.FindCommonSkills()
				.Where(intent => !intent.Behavior.SkipForChat);
			if (currentIntentId != null) {
				items = items.Where(x => x.UId != currentIntentId);
			}
			return items;
		}

		private IEnumerable<CopilotIntentSchema> FindCommonIntentsForApi() {
			return SkillSchemaService.FindCommonSkills().Where(intent => intent.Behavior.SkipForChat);
		}

		private LocalizableString GetLocalizableString(string localizableStringName) {
			string lsv = "LocalizableStrings." + localizableStringName + ".Value";
			return new LocalizableString(_userConnection.Workspace.ResourceStorage, nameof(CopilotEngine), lsv);
		}

		private IEnumerable<CopilotActionMetaItem> GetActionsMetaItems(Guid? currentIntentId) {
			var actionMetaItems = new List<CopilotActionMetaItem>();
			if (currentIntentId != SystemIntent?.UId) {
				IEnumerable<CopilotActionMetaItem> currentIntentActionsMetaItems =
					GetCurrentIntentActionsMetaItems(currentIntentId);
				actionMetaItems.AddRange(currentIntentActionsMetaItems);
			}
			if (!currentIntentId.IsNullOrEmpty()) {
				CopilotActionMetaItemCollection systemActionMetaItems = SystemIntent?.Actions;
				if (systemActionMetaItems != null && systemActionMetaItems.IsNotNullOrEmpty()) {
					actionMetaItems.AddRange(systemActionMetaItems);
				}
			}
			return actionMetaItems;
		}

		private IEnumerable<CopilotActionMetaItem> GetCurrentIntentActionsMetaItems(Guid? intentId) {
			if (intentId.IsNullOrEmpty()) {
				return new List<CopilotActionMetaItem>();
			}
			CopilotIntentSchema intent = SkillSchemaService.FindSchemaByUId(intentId.Value);
			if (intent != null && !SkillSchemaService.HasOperationPermitted(
					UserSchemaOperationRights.Execute, intent.UId)) {
				LocalizableString ls = GetLocalizableString("NoSkillExecutionRight");
				throw new SecurityException(ls.Format(intent.Name));
			}
			List<CopilotActionMetaItem> actions = intent?.Actions?.ToList();
			return actions ?? new List<CopilotActionMetaItem>();
		}

		private (List<ToolDefinition> tools, Dictionary<string, IToolExecutor> toolMapping) GetToolDefinitions(
				Guid? intentId) {
			IEnumerable<CopilotIntentSchema> commonIntents = FindCommonIntentsForChat(intentId);
			IEnumerable<CopilotActionMetaItem> actionMetaItems = GetActionsMetaItems(intentId);
			return _toolProcessor.GetToolDefinitions(actionMetaItems, commonIntents);
		}

		private ChatCompletionRequest CreateCompletionRequest(IEnumerable<CopilotMessage> sessionMessages,
				IList<ToolDefinition> tools = null) {
			List<ChatMessage> messages = sessionMessages
				.Select(msg => msg.ToCompletionApiMessage())
				.ToList();
			var completionRequest = new ChatCompletionRequest {
				Messages = messages,
				Tools = tools ?? Array.Empty<ToolDefinition>(),
				Temperature = null
			};
			return completionRequest;
		}

		private ChatCompletionRequest CreateCompletionRequest(string systemPrompt, string userMessageContent) {
			CopilotMessage systemMessage = CopilotMessage.FromSystem(systemPrompt);
			CopilotMessage userMessage = CopilotMessage.FromUser(userMessageContent);
			var completionRequest = new ChatCompletionRequest {
				Messages = new List<ChatMessage> {
					systemMessage.ToCompletionApiMessage(),
					userMessage.ToCompletionApiMessage()
				}
			};
			return completionRequest;
		}

		private bool IsActiveIntent(CopilotIntentSchema intent) {
			bool canDevelopIntents = _userConnection.DBSecurityEngine.GetCanExecuteOperation(
				CanDevelopCopilotIntentsOperation, new GetCanExecuteOperationOptions {
					ThrowExceptionIfNotFound = false
				});
			return intent.Status != CopilotIntentStatus.Deactivated &&
				(canDevelopIntents || intent.Status != CopilotIntentStatus.InDevelopment);
		}

		private async Task<(DateTime? start, DateTime? end, ChatCompletionResponse response)> ProcessCompletionRequest(
				ChatCompletionRequest request, bool sync = true, CancellationToken token = default) {
			DateTime? start = DateTime.Now;
			ChatCompletionResponse response;
			if (sync) {
				response = _completionService.ChatCompletion(request);
			} else {
				response = await _completionService.ChatCompletionAsync(request, token).ConfigureAwait(false);
			}
			DateTime? end = DateTime.Now;
			return (start, end, response);
		}

		private void HandleToolCalls(CopilotSession session, ChatCompletionResponse response,
			Dictionary<string, IToolExecutor> mapping, CopilotContext copilotContext) {
			List<CopilotMessage> messages = _toolProcessor.HandleToolCalls(response, session, mapping);
			HandleToolCallsCompleted(messages, session, copilotContext);
		}

		private static (string errorMessage, string errorCode) GetErrorInfo(Exception e) {
			if (e is GenAIException genAiException) {
				return (genAiException.RawError, genAiException.ErrorCode);
			}
			if (e is SecurityException) {
				return (e.Message, CopilotServiceErrorCode.InsufficientPermissions);
			}
			return (e.Message, CopilotServiceErrorCode.UnknownError);
		}

		private void SendSession(CopilotSession session, CopilotContext copilotContext = null) {
			lock (session) {
				DateTime? startDate = null, endDate = null;
				ChatCompletionResponse completionResponse = null;
				var errorMessage = string.Empty;
				(List<ToolDefinition> tools, Dictionary<string, IToolExecutor> toolMapping) = GetToolDefinitions(session.CurrentIntentId);
				ChatCompletionRequest completionRequest = CreateCompletionRequest(session.Messages, tools);
				var isFailed = false;
				try {
					SendMessagesToClient(session);
					_copilotMsgChannelSender.SendSessionProgress(CopilotSessionProgress.Create(_userConnection, session,
						CopilotSessionProgressStates.WaitingForAssistantMessage), session.UserId);
					(startDate, endDate, completionResponse) = ProcessCompletionRequest(completionRequest)
						.GetAwaiter()
						.GetResult();
					HandleCompletionResponse(completionResponse, session);
				} catch (Exception e) {
					(errorMessage, _) = GetErrorInfo(e);
					isFailed = true;
					throw;
				} finally {
					UsageResponse usage = completionResponse?.Usage;
					Guid requestId = SaveRequestInfo(startDate, endDate, usage, errorMessage, isFailed);
					_sessionManager.Update(session, requestId);
					_copilotMsgChannelSender.SendSessionProgress(CopilotSessionProgress.Create(_userConnection, session,
						CopilotSessionProgressStates.WaitingForUserMessage), session.UserId);
				}
				HandleToolCalls(session, completionResponse, toolMapping, copilotContext);
			}
		}

		private async Task<Guid> CompleteIntent(ChatCompletionRequest completionRequest,
				CopilotIntentCallResult response, CancellationToken token) {
			DateTime? startDate = null;
			DateTime? endDate = null;
			ChatCompletionResponse completionResponse = null;
			Guid requestId;
			try {
				(startDate, endDate, completionResponse) = await ProcessCompletionRequest(completionRequest,
						false, token)
					.ConfigureAwait(true);
				MapIntentResponse(completionResponse, response);
			} catch (Exception e) {
				(response.ErrorMessage, _) = GetErrorInfo(e);
				response.Status = IntentCallStatus.FailedToExecute;
			} finally {
				UsageResponse usage = completionResponse?.Usage;
				requestId = SaveRequestInfo(startDate, endDate, usage, response.ErrorMessage, !response.IsSuccess);
			}
			return requestId;
		}

		private static void MapIntentResponse(ChatCompletionResponse copilotResponse, CopilotIntentCallResult intentResponse) {
			if (copilotResponse?.Choices == null) {
				intentResponse.Status = IntentCallStatus.CantGenerateGoodResponse;
				return;
			}
			intentResponse.Status = IntentCallStatus.ExecutedSuccessfully;
			intentResponse.Content = GetCopilotMessage(copilotResponse);
		}

		private string GenerateIntentPrompt(IDictionary<string, object> parameterValues, string additionalPromptText,
			CopilotIntentSchema intent, List<string> warnings) {
			if (parameterValues == null) {
				parameterValues = new Dictionary<string, object>();
			}
			bool shouldInline = Features.GetIsEnabled<GenAIFeatures.UseInlineTemplateParameters>();
			List<string> intentInputParameters = intent.InputParameters
				.Select(x => x.Name)
				.ToList();
			HashSet<string> notSpecifiedKeys = GetNotSpecifiedParameters(parameterValues, intentInputParameters, warnings);
			GetExtraParameterNames(parameterValues, warnings, intentInputParameters);
			var inputParameters = new Dictionary<string, object>(parameterValues);
			var prompt = new StringBuilder();
			if (shouldInline) {
				prompt.Append(GetFormattedPrompt(parameterValues, intent, notSpecifiedKeys));
				inputParameters = inputParameters
					.Where(x => !intentInputParameters.Contains(x.Key))
					.ToDictionary(x => x.Key, x => x.Value);
			} else {
				prompt.Append(intent.Prompt);
				foreach (string key in notSpecifiedKeys) {
					inputParameters[key] = null;
				}
			}
			if (!string.IsNullOrWhiteSpace(additionalPromptText)) {
				prompt.Append(Environment.NewLine);
				prompt.Append(additionalPromptText);
			}
			AppendParameters(inputParameters, intent, prompt);
			return prompt.ToString();
		}

		private static void AppendParameters(Dictionary<string, object> inputParameterValues,
			CopilotIntentSchema intent, StringBuilder prompt) {
			var parameterSectionFormatter = new IntentParametersSectionFormatter();
			if (inputParameterValues.Count > 0) {
				prompt.Append(Environment.NewLine);
				prompt.Append(parameterSectionFormatter.FormatInputParameters(intent.InputParameters, inputParameterValues));
			}
			if (intent.OutputParameters.Count > 0) {
				prompt.Append(Environment.NewLine);
				prompt.Append(parameterSectionFormatter.FormatOutputParameters(intent.OutputParameters));
			}
		}

		private string GetFormattedPrompt(IDictionary<string, object> parameterValues, CopilotIntentSchema intent,
				HashSet<string> notSpecifiedKeys) {
			var inlineParameters = new Dictionary<string, object>(parameterValues);
			inlineParameters.AddRange(notSpecifiedKeys.ToDictionary(x => x, x => (object)string.Empty));
			return GenerateIntentPrompt(intent, inlineParameters);
		}

		private void GetExtraParameterNames(IDictionary<string, object> inputParameters,
				List<string> warnings, List<string> inputParameterNames) {
			HashSet<string> extraParameterNames = inputParameters.Keys.ToHashSet();
			extraParameterNames.ExceptWith(inputParameterNames);
			if (extraParameterNames.Any()) {
				string warning = GetLocalizableString("WarningParameterNotExist")
					.Format(string.Join(",", extraParameterNames));
				warnings.Add(warning);
			}
		}

		private HashSet<string> GetNotSpecifiedParameters(IDictionary<string, object> inputParameters,
				List<string> inputParameterNames, List<string> warnings) {
			var unSpecified = new HashSet<string>(inputParameterNames);
			unSpecified.ExceptWith(inputParameters.Keys);
			if (unSpecified.Any()) {
				string warning = GetLocalizableString("WarningParameterValueNotSpecified")
					.Format(string.Join(",", unSpecified));
				warnings.Add(warning);
			}
			return unSpecified;
		}

		private string GenerateIntentPrompt(CopilotIntentSchema intent, IDictionary<string, object> parameterValues) {
			var formattingContext = new PromptTemplateFormattingContext(_userConnection) {
				VariableValues = parameterValues
			};
			return intent.PromptTemplate.Format(formattingContext);
		}

		private List<CopilotMessage> CreateCopilotApiIntentMessages(string prompt) {
			string apiSystemPrompt = GetCopilotPrompt(ApiSystemPromptCode,
				_promptFactory.CreateSystemPrompt(SystemPromptTarget.Api));
			CopilotMessage systemMessage = CopilotMessage.FromSystem(apiSystemPrompt);
			CopilotMessage message = CopilotMessage.FromUser(prompt);
			return new List<CopilotMessage> {
				systemMessage,
				message
			};
		}

		private void UpdateContext(CopilotContext copilotContext, CopilotSession session) {
			if (session.CurrentIntentId.IsNullOrEmpty()) {
				return;
			}
			session.UpdateContext(copilotContext, _contextBuilder);
		}

		private EntityCollection FetchCopilotPrompt(string code) {
			var esq = new EntitySchemaQuery(_userConnection.EntitySchemaManager, "CopilotPrompt") {
				PrimaryQueryColumn = {
					IsAlwaysSelect = true
				},
				Cache = _userConnection.SessionCache.WithLocalCaching("CopilotPromptCache"),
				CacheItemName = $"Copilot{code}PromptCacheItem",
				UseAdminRights = false
			};
			esq.AddColumn("Prompt");
			esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "Code", code));
			EntityCollection entityCollection = esq.GetEntityCollection(_userConnection);
			return entityCollection;
		}

		private string GetCopilotPrompt(string code, string fallbackPrompt) {
			EntityCollection entityCollection = FetchCopilotPrompt(code);
			string prompt = string.Join(Environment.NewLine,
				entityCollection?.Select(x => x?.GetTypedColumnValue<string>("Prompt") ?? string.Empty) ??
				Array.Empty<string>());
			return _variableResolver.Resolve(prompt.IsNotNullOrEmpty() ? prompt : fallbackPrompt);
		}

		private void HandleOutputParameters(CopilotIntentCallResult response, CopilotIntentSchema intent) {
			if (response.Content.IsNullOrEmpty()) {
				throw new ArgumentNullOrEmptyException(nameof(response.Content));
			}
			Dictionary<string, string> outputParameters = ParseContent(response.Content);
			if (intent.OutputParameters.Count > 0) {
				response.ResultParameters = _outputParametersHandler.HandleOutputParameters(outputParameters,
					intent.OutputParameters);
			} else {
				response.Content = outputParameters["content"];
			}
		}

		private static Dictionary<string, string> ParseContent(string content) {
			if (content.StartsWith("```json")) {
				content = content.Substring(7, content.Length - 10);
			}
			return JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
		}

		#endregion

		#region Methods: Public

		/// <inheritdoc/>
		public CopilotChatPart SendUserMessage(string content, Guid? copilotSessionId = null,
				CopilotContext copilotContext = null) {
			_userConnection.DBSecurityEngine.CheckCanExecuteOperation(CanRunCopilotOperation);
			CopilotSession session = null;
			if (copilotSessionId.HasValue && copilotSessionId.Value.IsNotEmpty()) {
				session = _sessionManager.FindById(copilotSessionId.Value);
			}
			if (session == null) {
				session = CreateSession();
			}
			UpdateContext(copilotContext, session);
			CopilotMessage userMessage = CopilotMessage.FromUser(content);
			session.AddMessage(userMessage);
			DateTime lastMessageDate = userMessage.Date;
			string errorMessage = null;
			string errorCode = null;
			try {
				SendSession(session, copilotContext);
			} catch (Exception e) {
				(errorMessage, errorCode) = GetErrorInfo(e);
				_log.Error(e);
			}
			List<BaseCopilotMessage> lastMessages = session.Messages
				.Where(message => message.Date >= lastMessageDate)
				.Cast<BaseCopilotMessage>().ToList();
			return new CopilotChatPart(lastMessages, session, errorMessage, errorCode);
		}

		/// <inheritdoc/>
		public void CompleteAction(Guid copilotSessionId, string actionInstanceUId,
				CopilotActionExecutionResult result) {
			CopilotSession session = _sessionManager.FindById(copilotSessionId);
			if (session?.State != CopilotSessionState.Active) {
				return;
			}
			string resultContent = result.Status == CopilotActionExecutionStatus.Completed
				? result.Response ?? "Ok"
				: result.ErrorMessage ?? "Unknown error occurred";
			List<CopilotMessage> toolMessages = session.CreateToolCallMessages(actionInstanceUId, resultContent);
			HandleToolCallsCompleted(toolMessages, session);
		}

		/// <inheritdoc/>
		public async Task<CopilotIntentCallResult> ExecuteIntentAsync(CopilotIntentCall call,
				CancellationToken token = default) {
			call.CheckArgumentNull(nameof(call));
			var response = new CopilotIntentCallResult();
			try {
				_userConnection.DBSecurityEngine.CheckCanExecuteOperation(CanRunCopilotApiOperation);
			} catch (SecurityException e) {
				SetErrorResponse(response, e.GetFullMessage(), IntentCallStatus.InsufficientPermissions);
				return response;
			}
			if (Features.GetIsDisabled<GenAIFeatures.EnableStandaloneApi>()) {
				SetErrorResponse(response, GetLocalizableString("StandaloneApiFeatureOff"),
					IntentCallStatus.InsufficientPermissions);
				return response;
			}
			CopilotIntentSchema intent;
			try {
				intent = SkillSchemaService.FindSchemaByName(call.IntentName);
			} catch (SecurityException) {
				LocalizableString ls = GetLocalizableString("NoSkillReadRight");
				SetErrorResponse(response, ls.Format(call.IntentName),
					IntentCallStatus.InsufficientPermissions);
				return response;
			}
			if (intent == null) {
				LocalizableString ls = GetLocalizableString("IntentNotFound");
				SetErrorResponse(response, ls.Format(call.IntentName), IntentCallStatus.IntentNotFound);
				return response;
			}
			if (!SkillSchemaService.HasOperationPermitted(UserSchemaOperationRights.Execute, intent.UId)) {
				LocalizableString ls = GetLocalizableString("NoSkillExecutionRight");
				SetErrorResponse(response, ls.Format(call.IntentName),
					IntentCallStatus.InsufficientPermissions);
				return response;
			}
			if (!IsActiveIntent(intent)) {
				LocalizableString ls = GetLocalizableString("InactiveIntent");
				SetErrorResponse(response, ls.Format(call.IntentName), IntentCallStatus.InactiveIntent);
				return response;
			}
			if (!intent.Behavior.SkipForChat) {
				LocalizableString ls = GetLocalizableString("WrongIntentMode");
				SetErrorResponse(response, ls.Format(call.IntentName, CopilotIntentMode.Chat),
					IntentCallStatus.WrongIntentMode);
				return response;
			}
			var warnings = new List<string>();
			response.Warnings = warnings;
			string prompt = GenerateIntentPrompt(call.Parameters, call.AdditionalPromptText, intent, warnings);
			List<CopilotMessage> messages = CreateCopilotApiIntentMessages(prompt);
			var session = new CopilotSession(_userConnection.CurrentUser.Id, messages, intent.UId);
			_sessionManager.Add(session);
			ChatCompletionRequest completionRequest = CreateCompletionRequest(messages);
			Guid requestId = Guid.Empty;
			try {
				requestId = await CompleteIntent(completionRequest, response, token).ConfigureAwait(false);
				if (response.IsSuccess) {
					try {
						HandleOutputParameters(response, intent);
					} catch (Exception e) {
						response.Status = IntentCallStatus.ResponseParsingFailed;
						LocalizableString ls = GetLocalizableString("ParsingFailed");
						response.ErrorMessage = ls.Format(e.GetFullMessage());
					}
				}
				return response;
			} catch (Exception e) {
				SetErrorResponse(response, e.GetFullMessage());
				return response;
			} finally {
				_sessionManager.CloseSession(session, requestId);
			}
		}

		/// <inheritdoc/>
		public IList<string> GetAvailableIntents(CopilotIntentMode mode, params string[] names) {
			if ((mode == CopilotIntentMode.Api && !CanRunCopilotApi()) ||
					(mode == CopilotIntentMode.Chat && !CanUseCopilotChat())) {
				return new List<string>();
			}
			IEnumerable<CopilotIntentSchema> activeIntents = mode == CopilotIntentMode.Api ?
				FindCommonIntentsForApi() :
				FindCommonIntentsForChat(null);
			IList<string> availableIntentNames = names.Any() ?
				FilterActiveIntents(names, activeIntents).ToList() :
				activeIntents.Select(intent => intent.Name).ToList();
			return availableIntentNames;
		}

		/// <inheritdoc/>
		public CopilotIntentCallResult ExecuteIntent(CopilotIntentCall call) {
			return AsyncPump.Run(() => ExecuteIntentAsync(call, CancellationToken.None));
		}

		#endregion

	}

}

