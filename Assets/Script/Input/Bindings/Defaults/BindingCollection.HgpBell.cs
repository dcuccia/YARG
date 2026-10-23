using YARG.Core;
using YARG.Input.Devices;
using YARG.Integration.Hgp;

namespace YARG.Input
{
    public partial class BindingCollection
    {
        public bool SetDefaultBindings(HgpBellDevice bell)
        {
            // Bells have no menu controls; menus stay on the host keyboard/gamepad
            if (IsMenu || Mode != GameMode.ProKeys)
            {
                return false;
            }

            // Bell i -> five-lane key i (Green..Orange). Bells 6-8 are reserved for later gestures.
            for (int i = 0; i < HgpRingerSlots.BellActions.Length; i++)
            {
                AddBinding(HgpRingerSlots.BellActions[i], bell.Bells[i]);
            }

            return true;
        }
    }
}
