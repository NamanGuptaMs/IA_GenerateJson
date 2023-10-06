namespace GenerateArtifacts
{
    using Microsoft.AspNetCore.JsonPatch;
    using Microsoft.AspNetCore.JsonPatch.Operations;
    using Newtonsoft.Json.Linq;
    using System.Collections.Generic;
    using System.Linq;

    public static class Jsondiffpatch_
    {
        public static JsonPatchDocument Diff(JToken left, JToken right)
        {
            var document = new JsonPatchDocument();

            var operations = CalculatePatch(left, right);
            document.Operations.AddRange(operations);

            return document;
        }

        private static IEnumerable<Operation> CalculatePatch(JToken left, JToken right, string path = "")
        {
            if (left.Type != right.Type)
            {
                yield return Replace(path, "", right);
                yield break;
            }

            if (left.Type == JTokenType.Array)
            {
                if (JToken.DeepEquals(left, right))
                {
                    yield break;
                }

                var leftList = left.Children().ToList();
                var rightList = right.Children().ToList();

                var leftCount = leftList.Count;
                var rightCount = rightList.Count;

                var index = 0;

                while (index < leftList.Count && index < rightList.Count)
                {
                    foreach (var patch in CalculatePatch(leftList[index], rightList[index], Extend(path, (index++).ToString())))
                    {
                        yield return patch;
                    }
                }

                var needsToAdd = rightCount > leftCount;

                if (needsToAdd)
                {
                    while (index < rightCount)
                    {
                        yield return Add(Extend(path, index.ToString()), "", rightList[index]);
                        index++;
                    }
                }

                var needsToRemove = rightCount < leftCount;

                if (needsToRemove)
                {
                    index = leftCount;
                    while (index > rightCount)
                    {
                        yield return Remove(Extend(path, (--index).ToString()), "");
                    }
                }

                yield break;
            }

            if (left.Type == JTokenType.Object)
            {
                var leftProps = ((IDictionary<string, JToken>)left).OrderBy(p => p.Key);
                var rightProps = ((IDictionary<string, JToken>)right).OrderBy(p => p.Key);

                foreach (var removed in leftProps.Except(rightProps, KeyComparer.Instance))
                {
                    yield return Remove(path, removed.Key);
                }

                foreach (var added in rightProps.Except(leftProps, KeyComparer.Instance))
                {
                    yield return Add(path, added.Key, added.Value);
                }

                var matchedKeys = leftProps.Select(x => x.Key).Intersect(rightProps.Select(y => y.Key));
                var zipped = matchedKeys.Select(k => new { key = k, left = left[k], right = right[k] });

                foreach (var match in zipped)
                {
                    foreach (var patch in CalculatePatch(match.left, match.right, Extend(path, match.key)))
                    {
                        yield return patch;
                    }
                }

                yield break;
            }

            if (left.ToString() == right.ToString())
            {
                yield break;
            }

            yield return Replace(path, "", right);
        }

        private static string Extend(string path, string extension)
            => path + "/" + extension;


        private static Operation CreateOperationFrom(string op, string path, string key, JToken value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return new Operation { op = op, path = path, value = value };
            }

            return new Operation { op = op, path = Extend(path, key), value = value };
        }

        private static Operation Add(string path, string key, JToken value)
            => CreateOperationFrom("add", path, key, value);

        private static Operation Remove(string path, string key)
            => CreateOperationFrom("remove", path, key, null);

        private static Operation Replace(string path, string key, JToken value)
            => CreateOperationFrom("replace", path, key, value);

        private class KeyComparer : IEqualityComparer<KeyValuePair<string, JToken>>
        {
            public static readonly KeyComparer Instance = new KeyComparer();

            public bool Equals(KeyValuePair<string, JToken> x, KeyValuePair<string, JToken> y)
                => x.Key.Equals(y.Key);

            public int GetHashCode(KeyValuePair<string, JToken> obj)
                => obj.Key.GetHashCode();
        }
    }
}