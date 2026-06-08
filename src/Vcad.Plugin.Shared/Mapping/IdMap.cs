using System.Collections.Generic;
using Vcad.Core.Results;

namespace Vcad.Plugin.Mapping
{
    internal class IdMap
    {
        private readonly Dictionary<string, EntityRef> _byDslId = new Dictionary<string, EntityRef>();

        public void Add(string dslId, EntityRef entity)
        {
            if (string.IsNullOrEmpty(dslId) || entity == null) return;
            _byDslId[dslId] = entity;
        }

        public bool TryGet(string dslId, out EntityRef entity) => _byDslId.TryGetValue(dslId, out entity);

        public IEnumerable<KeyValuePair<string, EntityRef>> All() => _byDslId;

        public int Count => _byDslId.Count;
    }
}
