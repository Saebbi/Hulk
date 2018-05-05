using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace Hulk
{
    public class HediffWithComps_HulkExtraInfo : HediffWithComps
    {
        public CompHulk CompHulk => this.pawn.TryGetComp<CompHulk>();

        public override string TipStringExtra
        {
            get
            {
                StringBuilder s = new StringBuilder();
                s.AppendLine("ROM_FormHealth_Tooltip".Translate(CompHulk.CurrentHulkForm.FormHealthScale * 100));
                s.AppendLine("ROM_FormSize_Tooltip".Translate(CompHulk.CurrentHulkForm.FormBodySize * 100));
                s.AppendLine("ROM_FormDmg_Tooltip".Translate(CompHulk.CurrentHulkForm.DmgImmunity * 100));
                s.AppendLine("---");
                string str = base.TipStringExtra;
                if (str != "")
                    s.Append(base.TipStringExtra);
                return s.ToString().TrimEndNewlines();
            }
        }
    }
}
