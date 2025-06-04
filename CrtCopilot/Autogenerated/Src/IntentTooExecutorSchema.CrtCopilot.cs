namespace Terrasoft.Configuration
{

	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.Globalization;
	using Terrasoft.Common;
	using Terrasoft.Core;
	using Terrasoft.Core.Configuration;

	#region Class: IntentTooExecutorSchema

	/// <exclude/>
	public class IntentTooExecutorSchema : Terrasoft.Core.SourceCodeSchema
	{

		#region Constructors: Public

		public IntentTooExecutorSchema(SourceCodeSchemaManager sourceCodeSchemaManager)
			: base(sourceCodeSchemaManager) {
		}

		public IntentTooExecutorSchema(IntentTooExecutorSchema source)
			: base( source) {
		}

		#endregion

		#region Methods: Protected

		protected override void InitializeProperties() {
			base.InitializeProperties();
			UId = new Guid("32ac06c1-0074-47df-915e-c3c36f9ad7a3");
			Name = "IntentTooExecutor";
			ParentSchemaUId = new Guid("50e3acc0-26fc-4237-a095-849a1d534bd3");
			CreatedInPackageId = new Guid("7a3a8162-4be1-46b5-bd50-b3efc2df6d2e");
			ZipBody = new byte[] { 31,139,8,0,0,0,0,0,4,0,117,83,193,110,219,48,12,61,43,64,254,129,203,118,112,176,192,185,103,89,138,193,221,134,0,237,80,204,235,121,80,109,54,209,38,75,134,36,103,11,2,255,251,104,75,74,236,54,187,196,16,201,199,199,247,200,40,94,161,173,121,129,144,25,228,78,232,52,211,181,144,218,77,39,167,233,132,53,86,168,29,228,71,235,176,162,140,148,88,80,141,178,233,87,84,104,68,241,97,58,161,170,183,6,119,20,133,76,114,107,87,176,85,14,149,251,161,181,252,252,23,139,198,105,211,87,45,151,75,88,219,166,170,184,57,110,194,59,22,128,126,6,183,199,0,181,224,8,108,211,8,90,14,80,117,243,36,69,1,69,71,117,133,9,136,126,204,204,58,29,172,54,226,192,29,2,137,44,181,146,71,8,50,125,135,188,216,99,197,225,167,80,214,113,85,160,151,21,185,94,179,36,215,208,17,60,135,158,145,157,187,193,71,24,52,102,172,245,221,163,107,247,232,246,186,36,223,30,122,186,17,245,157,176,110,29,200,238,209,90,190,195,77,48,13,19,235,76,183,157,206,171,140,75,185,45,23,112,43,250,253,144,87,107,159,93,128,126,250,69,75,219,0,55,187,166,234,204,93,64,63,30,11,109,115,106,219,141,97,253,55,78,31,158,105,142,46,107,140,33,160,23,155,156,101,165,143,219,114,222,11,98,7,110,160,242,227,89,82,171,240,207,245,201,79,35,230,16,78,191,24,93,117,238,38,62,203,222,205,242,223,66,74,32,26,227,176,92,193,55,58,210,21,156,46,204,93,160,93,64,198,235,78,237,40,21,98,148,157,193,251,115,195,91,180,133,17,175,171,189,166,65,22,110,110,46,103,144,14,18,109,58,139,198,177,139,227,243,197,127,21,249,63,205,192,174,7,10,214,110,222,3,218,139,111,116,200,238,209,162,9,88,178,47,90,255,29,43,125,192,187,113,62,9,150,139,103,72,94,66,223,144,245,141,148,113,133,44,174,36,253,84,150,47,139,67,155,182,255,53,232,26,163,206,43,28,93,41,170,210,31,106,255,246,209,113,176,253,7,95,63,113,182,69,4,0,0 };
		}

		#endregion

		#region Methods: Public

		public override void GetParentRealUIds(Collection<Guid> realUIds) {
			base.GetParentRealUIds(realUIds);
			realUIds.Add(new Guid("32ac06c1-0074-47df-915e-c3c36f9ad7a3"));
		}

		#endregion

	}

	#endregion

}

