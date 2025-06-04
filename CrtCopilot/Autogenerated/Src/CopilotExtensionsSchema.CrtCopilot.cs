namespace Terrasoft.Configuration
{

	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.Globalization;
	using Terrasoft.Common;
	using Terrasoft.Core;
	using Terrasoft.Core.Configuration;

	#region Class: CopilotExtensionsSchema

	/// <exclude/>
	public class CopilotExtensionsSchema : Terrasoft.Core.SourceCodeSchema
	{

		#region Constructors: Public

		public CopilotExtensionsSchema(SourceCodeSchemaManager sourceCodeSchemaManager)
			: base(sourceCodeSchemaManager) {
		}

		public CopilotExtensionsSchema(CopilotExtensionsSchema source)
			: base( source) {
		}

		#endregion

		#region Methods: Protected

		protected override void InitializeProperties() {
			base.InitializeProperties();
			UId = new Guid("84f2730c-7d56-495e-9fdb-10feef1e0da0");
			Name = "CopilotExtensions";
			ParentSchemaUId = new Guid("50e3acc0-26fc-4237-a095-849a1d534bd3");
			CreatedInPackageId = new Guid("ed753793-30d5-4797-a3f9-3019dcc6e358");
			ZipBody = new byte[] { 31,139,8,0,0,0,0,0,4,0,141,146,193,110,194,48,12,134,207,69,226,29,34,113,41,151,60,192,54,216,129,141,169,210,208,144,96,218,57,107,13,68,43,78,103,187,211,16,218,187,47,73,11,90,129,77,187,84,201,111,251,179,127,55,104,182,192,149,201,65,77,8,140,88,167,39,174,178,165,147,126,111,223,239,37,53,91,92,171,197,142,5,182,250,209,226,251,245,81,92,2,145,97,183,18,95,65,224,117,31,177,40,64,104,74,197,226,89,185,202,75,195,172,90,226,253,167,0,178,117,200,62,115,31,243,147,1,193,218,43,106,6,178,113,5,95,169,121,253,90,218,188,9,86,241,124,96,181,148,204,183,64,89,228,27,216,154,153,65,179,6,82,15,112,73,78,101,99,89,61,51,208,196,33,66,46,161,81,221,185,14,85,48,153,36,4,82,211,105,80,191,56,122,139,187,209,29,238,156,220,135,45,128,180,111,219,74,55,191,15,55,78,135,97,103,201,215,63,61,169,169,197,162,89,120,35,55,54,254,112,111,207,181,131,175,172,163,102,158,121,105,210,113,7,17,178,212,232,18,53,24,14,81,78,135,145,158,232,169,37,150,39,186,131,149,169,75,73,109,44,245,184,240,90,50,244,38,209,47,111,185,171,64,141,70,93,11,65,212,109,93,179,160,195,79,56,157,229,246,72,250,185,199,1,96,209,188,157,120,143,170,255,124,3,106,89,66,243,207,2,0,0 };
		}

		#endregion

		#region Methods: Public

		public override void GetParentRealUIds(Collection<Guid> realUIds) {
			base.GetParentRealUIds(realUIds);
			realUIds.Add(new Guid("84f2730c-7d56-495e-9fdb-10feef1e0da0"));
		}

		#endregion

	}

	#endregion

}

