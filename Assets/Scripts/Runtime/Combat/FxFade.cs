using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>How an effect fades over its life.</summary>
    public enum FxFade
    {
        /// <summary>Steady fade from start to end.</summary>
        Linear,
        /// <summary>Bright at first, gone quickly (flashes).</summary>
        Quick,
        /// <summary>Fully visible for most of its life, fades at the end.</summary>
        Late,
        /// <summary>Fades in, holds, fades out.</summary>
        InOut,
        /// <summary>Fully visible until it ends (falling swords and meteors).</summary>
        None,
    }
}
