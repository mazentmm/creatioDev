namespace Creatio.Copilot
{
	using System.Collections.Generic;

	#region Class: IntentToolExecutor

	/// <summary>
	/// Executor of the Intents tools.
	/// </summary>
	public class IntentToolExecutor : IToolExecutor
	{
		private readonly CopilotIntentSchema _instance;

		public IntentToolExecutor(CopilotIntentSchema instance) {
			_instance = instance;
		}

		#region Methods: Public

		public List<CopilotMessage> Execute(string toolCallId, Dictionary<string, object> arguments, 
				CopilotSession session) {
			session.SetCurrentIntent(_instance.UId);
			var messages = new List<CopilotMessage> {
				CopilotMessage.FromTool(
					$"Skill started: Name: {_instance.Name}, Caption: {_instance.Caption}, " +
					$"Description: {_instance.IntentDescription ?? _instance.Description}.", 
					toolCallId),
				CopilotMessage.FromSystem(_instance.Prompt)
			};
			var lastUserMessage = session.RemoveLastUserMessage();
			if (lastUserMessage != null) {
				messages.Add(lastUserMessage);
			}
			return messages;
		}

		#endregion

	}

	#endregion

}
